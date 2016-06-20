﻿using Rubberduck.RegexAssistant.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rubberduck.RegexAssistant
{
    public interface IRegularExpression : IDescribable
    {
        Quantifier Quantifier { get; }

        bool TryMatch(string text, out string remaining);
    }

    public class ConcatenatedExpression : IRegularExpression
    {
        private readonly Quantifier quant;
        internal readonly IList<IRegularExpression> Subexpressions;

        public ConcatenatedExpression(IList<IRegularExpression> subexpressions)
        {
            Subexpressions = subexpressions;
            quant = new Quantifier(""); // these are always exactly once. Quantifying happens through groups
        }

        public string Description
        {
            get
            {
                return string.Join(", ", Subexpressions.Select(exp => exp.Description)) + " " + Quantifier.HumanReadable();
            }
        }

        public Quantifier Quantifier
        {
            get
            {
                return quant;
            }
        }

        public bool TryMatch(string text, out string remaining)
        {
            throw new NotImplementedException();
        }
    }

    public class AlternativesExpression : IRegularExpression
    {
        private readonly Quantifier quant;
        internal readonly IList<IRegularExpression> Subexpressions;

        public AlternativesExpression(IList<IRegularExpression> subexpressions)
        {
            Subexpressions = subexpressions;
            quant = new Quantifier(""); // these are always exactly once. Quantifying happens through groups
        }

        public string Description
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public Quantifier Quantifier
        {
            get
            {
                return quant;
            }
        }

        public bool TryMatch(string text, out string remaining)
        {
            throw new NotImplementedException();
        }
    }

    public class SingleAtomExpression : IRegularExpression
    {
        public readonly Atom Atom;
        private readonly Quantifier quant;

        public SingleAtomExpression(Atom atom, Quantifier quant)
        {
            Atom = atom;
            this.quant = quant;
        }

        public string Description
        {
            get
            {
                return string.Format("{0} {1}.", Atom.Description, Quantifier.HumanReadable());
            }
        
        }

        public Quantifier Quantifier
        {
            get
            {
                return quant;
            }
        }

        public bool TryMatch(string text, out string remaining)
        {
            // try to match the atom a given number of times.. 
            throw new NotImplementedException();
        }

        public override bool Equals(object obj)
        {
            if (obj is SingleAtomExpression)
            {
                SingleAtomExpression other = obj as SingleAtomExpression;
                return other.Atom.Equals(Atom) && other.Quantifier.Equals(Quantifier);
            }
            return false;
        }
    }
        
    public static class RegularExpression
    {
        public static IRegularExpression Parse(string specifier)
        {
            /*
             We basically run a Chain of Responsibility here. At the outermost level, we need to check whether this is an AlternativesExpression.
             If it isn't, we assume it's a ConcatenatedExpression and proceed to create one of these.
             The next step is attempting to parse Atoms. Those are packed into a SingleAtomExpression with their respective Quantifier.

             Note that Atoms can request a Parse of their subexpressions. Prominent example here would be Groups.
             Also note that this here is responsible for separating atoms and Quantifiers. When we matched an Atom we need to try to match a Quantifier and pack them together. 
             If there is no Quantifier following (either because the input is exhausted or there directly is the next atom) then we instead pair with `new Quantifier("")`
             */
            // KISS: Alternatives is when you can split at 
            // grab all indices where we have a pipe
            List<int> pipeIndices = GrabPipeIndices(specifier);
            // and now weed out those inside character classes or groups
            WeedPipeIndices(ref pipeIndices, specifier);
            if (pipeIndices.Count == 0)
            { // assume ConcatenatedExpression when trying to parse all as a single atom fails
                IRegularExpression expression;
                string specifierCopy = specifier;
                if (TryParseAsAtom(ref specifierCopy, out expression) && specifierCopy.Length == 0)
                {
                    return expression;
                }
                else
                {
                    return ParseIntoConcatenatedExpression(specifier);
                }
            }
            else
            {
                return ParseIntoAlternativesExpression(pipeIndices, specifier); 
            }
        }

        //private static readonly ISet<char> escapeCharacters
        // successively parse everything into atoms
        private static IRegularExpression ParseIntoConcatenatedExpression(string specifier)
        {
            List<IRegularExpression> subexpressions = new List<IRegularExpression>();
            string currentSpecifier = specifier;
            while (currentSpecifier.Length > 0)
            {
                IRegularExpression expression;
                if (TryParseAsAtom(ref currentSpecifier, out expression))
                {
                    subexpressions.Add(expression);
                }
            }
            //subexpressions.Reverse();
            return new ConcatenatedExpression(subexpressions);
        }

        private static readonly Regex groupWithQuantifier = new Regex("^" + Group.Pattern + Quantifier.Pattern + "?");
        private static readonly Regex characterClassWithQuantifier = new Regex("^" + CharacterClass.Pattern + Quantifier.Pattern + "?");
        private static readonly Regex literalWithQuantifier = new Regex("^" + Literal.Pattern + Quantifier.Pattern + "?");
        internal static bool TryParseAsAtom(ref string specifier, out IRegularExpression expression)
        {
            Match m = groupWithQuantifier.Match(specifier);
            if (m.Success)
            {
                string atom = m.Groups["expression"].Value;
                string quantifier = m.Groups["quantifier"].Value;
                specifier = specifier.Substring(atom.Length + 2 + quantifier.Length);
                expression = new SingleAtomExpression(new Group("("+atom+")"), new Quantifier(quantifier));
                return true;
            }
            m = characterClassWithQuantifier.Match(specifier);
            if (m.Success)
            {
                string atom = m.Groups["expression"].Value;
                string quantifier = m.Groups["quantifier"].Value;
                specifier = specifier.Substring(atom.Length + 2 + quantifier.Length);
                expression = new SingleAtomExpression(new CharacterClass("["+atom+"]"), new Quantifier(quantifier));
                return true;
            }
            m = literalWithQuantifier.Match(specifier);
            if (m.Success)
            {
                string atom = m.Groups["expression"].Value;
                string quantifier = m.Groups["quantifier"].Value;
                specifier = specifier.Substring(atom.Length + quantifier.Length);
                expression = new SingleAtomExpression(new Literal(atom), new Quantifier(quantifier));
                return true;
            }
            expression = null;
            return false;
        }

        private static IRegularExpression ParseIntoAlternativesExpression(List<int> pipeIndices, string specifier)
        {
            List<IRegularExpression> expressions = new List<IRegularExpression>();
            string currentRemainder = specifier;
            for (int i = pipeIndices.Count - 1; i > 0; i--)
            {
                expressions.Add(Parse(currentRemainder.Substring(pipeIndices[i] + 1)));
                currentRemainder = currentRemainder.Substring(0, pipeIndices[i] - 1);
            }
            expressions.Reverse(); // because we built them from the back
            return new AlternativesExpression(expressions);
        }


        /// <summary>
        /// Finds all Pipes in the given specifier that are not escaped
        /// </summary>
        /// <param name="specifier">the regex specifier to search for unescaped pipes</param>
        /// <returns>A list populated with the indices of all pipes</returns>
        private static List<int> GrabPipeIndices(string specifier)
        {
            if (!specifier.Contains("|")) {
                return new List<int>();
            }
            int currentIndex = 0;
            List<int> result = new List<int>();
            while (true)
            {
                currentIndex = specifier.IndexOf("|", currentIndex);
                if(currentIndex == -1)
                {
                    break;
                }
                if (!specifier.Substring(currentIndex - 1, 2).Equals("\\|"))
                {
                    result.Add(currentIndex);
                }
            }
            return result;
        }

        /// <summary>
        /// Weeds out pipe indices that do not signify alternatives at the current "top level" from the given String.
        /// </summary>
        /// <param name="pipeIndices">indices of unescaped pipes in the given specifier</param>
        /// <param name="specifier">the regex specifier under scrutiny</param>
        private static void WeedPipeIndices(ref List<int> pipeIndices, string specifier)
        {
            if (pipeIndices.Count == 0)
            {
                return;
            }
            foreach (int pipeIndex in pipeIndices)
            {
                // must not be between () or [] braces, else we just weed it out
                
            }
        }
    }
}