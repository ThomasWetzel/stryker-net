using System.IO.Abstractions;
using Stryker.Core.Exceptions;

namespace Stryker.Core.Options.Inputs
{
    public class OutputPathInput : Input<string>
    {
        protected override string Description => string.Empty;

        public override string Default => null;

        public string Validate(IFileSystem fileSystem)
        {
            if (string.IsNullOrWhiteSpace(SuppliedInput))
            {
                throw new InputException("Outputpath can't be null or whitespace");
            }
            if (!fileSystem.Directory.Exists(SuppliedInput))
            {
                throw new InputException("Outputpath should exist");
            }
            return SuppliedInput;
        }
    }
}
