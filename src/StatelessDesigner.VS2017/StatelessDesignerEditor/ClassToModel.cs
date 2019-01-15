using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using EnvDTE;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using StatelessXml;
using Project = EnvDTE.Project;

namespace mrtn.StatelessDesignerEditor
{
    static class ClassToModel
    {
        class Item1EqualityComparer<T,U> : IEqualityComparer<(T, U)>
        {
            private Item1EqualityComparer() { }

            public static readonly Item1EqualityComparer<T, U> Instance = new Item1EqualityComparer<T, U>();

            public bool Equals((T, U) x, (T, U) y) => x.Item1.Equals(y.Item1);

            public int GetHashCode((T, U) obj) => obj.Item1.GetHashCode();
        }
        
        // es is assumed to be a member access expression
        static string ExtractMemberName(ExpressionSyntax es) =>
            (es as MemberAccessExpressionSyntax).Name.Identifier.ValueText;

        public static XmlModel TryLoadViaDTE(string xmlSource)
        {
            // PoC, definitely suboptimal.
            string methodName, className;
            try
            {
                var linkElement = XElement.Parse(xmlSource);
                if (linkElement.Name != (XmlParser.ns + "link")) return null;
                className = linkElement.Attribute("class").Value;
                methodName = linkElement.Attribute("method")?.Value ?? "ConfigureStateMachine";
            }
            catch
            {
                return null;
            }

            var dte = (DTE) ServiceProvider.GlobalProvider.GetService(typeof(DTE));

            CodeFunction vsfun = null;

            foreach(Project proj in dte.Solution.Projects)
            {
                var cls = proj.CodeModel?.CodeTypeFromFullName(className);
                if(cls == null) continue;
                foreach (var member in cls.Members)
                {
                    var f = member as CodeFunction;
                    if (f?.Name == methodName && f.InfoLocation == vsCMInfoLocation.vsCMInfoLocationProject)
                    {
                        vsfun = f;
                        break;
                    }
                }
                if(vsfun != null) break;
            }

            if (vsfun == null) return null;

            var workspace =
                ((IComponentModel)Package.GetGlobalService(typeof(SComponentModel)))
                .GetService<VisualStudioWorkspace>();

            var doc = workspace.CurrentSolution.GetDocument(
                    workspace.CurrentSolution.GetDocumentIdsWithFilePath(vsfun.ProjectItem.Document.FullName).First());

            var root = doc.GetSyntaxRootAsync().Result;
            var model = doc.GetSemanticModelAsync().Result;

            var (msyn, msym) = (
                from syn in model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                where syn.Identifier.ValueText == methodName
                select (syn, model.GetDeclaredSymbol(syn))).First();

            var localsyms =
                from p in (
                    from id in msyn.Body.DescendantNodes().OfType<IdentifierNameSyntax>()
                    let symi = model.GetSymbolInfo(id)
                    where symi.Symbol != null
                    select (symi.Symbol, id)
                ).Distinct(Item1EqualityComparer<ISymbol, IdentifierNameSyntax>.Instance)
                let ms = p.Item1
                let id = p.Item2
                where ms is IFieldSymbol || ms is IPropertySymbol || ms is ILocalSymbol
                let tpe = model.GetTypeInfo(id)
                where tpe.Type.MetadataName == "StateMachine`2" && tpe.Type.ContainingNamespace.Name == "Stateless"
                select ms;

            if (localsyms.Count() != 1) return null; // only 1 StateMachine-typed variable/field/property may be accessed

            var smsym = localsyms.First();

            var cfgs =
                from id in msyn.Body.DescendantNodes().OfType<IdentifierNameSyntax>()
                where model.GetSymbolInfo(id).Symbol?.Equals(smsym) ?? false
                let p = id.Parent as MemberAccessExpressionSyntax
                where p != null && ExtractMemberName(p) == "Configure"
                select (InvocationExpressionSyntax)p.Parent;
            
            var transitions = new HashSet<Transition>();

            foreach (var cfg in cfgs)
            {
                var fromState = ExtractMemberName(cfg.ArgumentList.Arguments[0].Expression);

                var trs = WalkConfigurationExpression(cfg);
                foreach (var tr in trs)
                {
                    tr.From = fromState;
                    if (tr.To == tr.Trigger) tr.Trigger = "";
                }

                transitions.UnionWith(trs);
            }

            var states = transitions.SelectMany(tr => new[] {tr.From, tr.To}).ToImmutableHashSet();
            var triggers = transitions.Select(tr => tr.Trigger).ToImmutableHashSet();

            return new XmlModel(null, null, null, triggers, null, null, states, null, transitions);
        }

        static ImmutableHashSet<Transition> WalkConfigurationExpression(InvocationExpressionSyntax syn, ImmutableHashSet<Transition> acc = null)
        {
            var pa = syn?.Parent as MemberAccessExpressionSyntax;
            var pi = pa?.Parent as InvocationExpressionSyntax;

            Transition transition = null;

            if (pa != null && pi != null)
            {
                string Trigger = null, To = null;
                switch (pa.Name.Identifier.ValueText)
                {
                    case "Permit":
                        {
                            if (pi.ArgumentList.Arguments.Count == 1)
                            {
                                Trigger = To = ExtractMemberName(pi.ArgumentList.Arguments[0].Expression);
                            }
                            else if (pi.ArgumentList.Arguments.Count == 2)
                            {
                                Trigger = ExtractMemberName(pi.ArgumentList.Arguments[0].Expression);
                                To = ExtractMemberName(pi.ArgumentList.Arguments[1].Expression);
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                            break;
                        }
                    case "PermitIf":
                        {
                            if (pi.ArgumentList.Arguments.Count == 1)
                            {
                                Trigger = To = ExtractMemberName(pi.ArgumentList.Arguments[0].Expression);
                            }
                            else if (pi.ArgumentList.Arguments.Count == 2)
                            {
                                Trigger = ExtractMemberName(pi.ArgumentList.Arguments[0].Expression);
                                To = ExtractMemberName(pi.ArgumentList.Arguments[1].Expression);
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                            break;
                        }
                }
                
                if (Trigger != null)
                {
                    if (To == null) throw new InvalidOperationException($"{nameof(To)} is unexpected to be null");
                    return WalkConfigurationExpression(pi, (acc ?? ImmutableHashSet<Transition>.Empty).Add(new Transition() { To = To, Trigger = Trigger }));
                }
                else
                {
                    return WalkConfigurationExpression(pi, acc);
                }
            }
            else
            {
                return acc ?? ImmutableHashSet<Transition>.Empty;
            }
        }
    }
}
