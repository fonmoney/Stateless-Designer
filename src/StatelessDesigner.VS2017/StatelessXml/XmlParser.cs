using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace StatelessXml
{
    public static class XmlParser
    {
        public static readonly XNamespace ns = "http://statelessdesigner.codeplex.com/Schema";

        /// <exception cref="NullReferenceException">If <paramref name="xmlContent"/> is <c>null</c> or it contains a malformed document.</exception>
        public static XmlModel Parse(string xmlContent)
        {
            var xElement = XElement.Parse(xmlContent);

            if (xElement.Name != ns + "statemachine") return null;

            var ItemName = xElement
                .Descendants(ns + "itemname")
                .First()
                .Value;

            var NameSpace = xElement
                .Descendants(ns + "namespace")
                .First()
                .Value;

            var ClassType = xElement
                .Descendants(ns + "class")
                .First()
                .Value;

            var TriggerTypeName = xElement.Descendants(ns + "triggers").First().Attribute("fromEnum")?.Value;
            if (TriggerTypeName == String.Empty) TriggerTypeName = null;

            var Triggers = (TriggerTypeName == null) ?
                (from e in xElement.Descendants(ns + "trigger") select e.Value).ToArray() :
                (
                    from e in xElement.Descendants(ns + "transition")
                    select (e.Attribute("trigger") ?? e.Attribute("to")).Value
                ).ToArray();

            var StateTypeName = xElement.Descendants(ns + "states").First().Attribute("fromEnum")?.Value;
            if (StateTypeName == String.Empty) StateTypeName = null;

            var States = (StateTypeName == null)
                ? (from e in xElement.Descendants(ns + "state") select e.Value).ToArray()
                : xElement.Descendants(ns + "transition")
                    .SelectMany(t => new[] {t.Attribute("from").Value, t.Attribute("to").Value}).Distinct().ToArray();

            var StartState = xElement.Descendants(ns + "states").First().Attribute("startState").Value;

            var Transitions = (
                from xElem in xElement.Descendants(ns + "transition")
                select new Transition
                {
                    Trigger = xElem.Attribute("trigger")?.Value ?? xElem.Attribute("To").Value,
                    From = xElem.Attribute("from").Value,
                    To = xElem.Attribute("to").Value
                }).ToArray();

            return new XmlModel(ItemName, NameSpace, ClassType, Triggers, TriggerTypeName, StateTypeName, States, StartState, Transitions);
        }
    }
}
