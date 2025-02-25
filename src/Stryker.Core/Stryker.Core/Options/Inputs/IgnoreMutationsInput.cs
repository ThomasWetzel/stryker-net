using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Core.Exceptions;
using Stryker.Core.Mutators;

namespace Stryker.Core.Options.Inputs
{
    public class IgnoreMutationsInput : Input<IEnumerable<string>>
    {
        public override IEnumerable<string> Default => Enumerable.Empty<string>();

        protected override string Description => @"The given mutators will be excluded for this mutation testrun.
    This argument takes a json array as value. Example: ['string', 'logical']";

        public IEnumerable<Mutator> Validate()
        {
            if (SuppliedInput is { })
            {
                var excludedMutators = new List<Mutator>();

                // Get all mutatorTypes and their descriptions
                var typeDescriptions = Enum.GetValues(typeof(Mutator))
                    .Cast<Mutator>()
                    .ToDictionary(x => x, x => x.GetDescription());

                foreach (var mutatorToExclude in SuppliedInput.Where(w => !w.Contains(".")))
                {
                    // Find any mutatorType that matches the name passed by the user
                    var mutatorDescriptor = typeDescriptions.FirstOrDefault(
                        x => x.Value.ToString().ToLower().Contains(mutatorToExclude.ToLower()));
                    if (mutatorDescriptor.Value is { })
                    {
                        excludedMutators.Add(mutatorDescriptor.Key);
                    }
                    else
                    {
                        throw new InputException($"Invalid excluded mutation ({mutatorToExclude}). The excluded mutations options are [{string.Join(", ", typeDescriptions.Select(x => x.Key))}]");
                    }
                }

               return excludedMutators;
            }
            return Enumerable.Empty<Mutator>();
        }


        public IEnumerable<LinqExpression> ValidateLinqExpressions()
        {
            if (SuppliedInput is { } && SuppliedInput.Any(w => w.ToLower().StartsWith("linq.")))
            {
                var linqSuppliedInput = SuppliedInput.Where(w => w.ToLower().StartsWith("linq."));

                var excludedLinqExpressions = new List<LinqExpression>();

                // Get all LinqExpression
                var linqExpressions = Enum.GetValues(typeof(LinqExpression))
                    .Cast<LinqExpression>()
                    .Where(w => w != LinqExpression.None);

                //Remove the 'linq.'
                var linqExpressionsInput = linqSuppliedInput
                                        .Select(s => s.Substring(5));

                foreach (var linqExpressionToExclude in linqExpressionsInput)
                {
                    // Validate if the input is a valid LinqExpression
                    if (!linqExpressions.Any(x => x.ToString().ToLower().Equals(linqExpressionToExclude.ToLower())))
                    {
                        throw new InputException($"Invalid excluded linq expression ({linqExpressionToExclude}). The excluded linq expression options are [{string.Join(", ", linqExpressions.Select(x => x.ToString()))}]");
                    }

                    // Find the LinqExpression that matches the name passed by the user
                    var linqExpression = linqExpressions.FirstOrDefault(x => x.ToString().ToLower().Equals(linqExpressionToExclude.ToLower()));
                    excludedLinqExpressions.Add(linqExpression);
                }

                return excludedLinqExpressions;
            }
            return Enumerable.Empty<LinqExpression>();
        }
    }
}
