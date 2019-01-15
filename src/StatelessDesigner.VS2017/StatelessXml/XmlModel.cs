using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatelessXml
{
    public class XmlModel
    {
        public string ItemName { get; }

        public string NameSpace { get; }

        public string ClassType { get; }

        public IEnumerable<string> Triggers { get; }

        public string TriggerTypeName { get; }

        public string StateTypeName { get; }

        public IEnumerable<string> States { get; }

        public string StartState { get; }

        public IEnumerable<Transition> Transitions { get; }

        public XmlModel(string itemName, string nameSpace, string classType, IEnumerable<string> triggers, string triggerTypeName, string stateTypeName, IEnumerable<string> states, string startState, IEnumerable<Transition> transitions)
        {
            ItemName = itemName;
            NameSpace = nameSpace;
            ClassType = classType;
            Triggers = triggers;
            TriggerTypeName = triggerTypeName;
            StateTypeName = stateTypeName;
            States = states;
            StartState = startState;
            Transitions = transitions;
        }
    }

    public class Transition
    {
        public string Trigger { get; set; }
        public string From { get; set; }
        public string To { get; set; }
    }
}
