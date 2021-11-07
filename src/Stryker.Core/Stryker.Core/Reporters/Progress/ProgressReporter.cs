using Stryker.Core.Mutants;
using Stryker.Core.ProjectComponents;
using Stryker.Core.ProjectComponents.TestProjects;
using System.Collections.Generic;
using System.Linq;

namespace Stryker.Core.Reporters.Progress
{
    public class ProgressReporter : IReporter
    {
        private readonly IProgressBarReporter _progressBarReporter;
        public ProgressReporter(IProgressBarReporter progressBarReporter)
        {
            _progressBarReporter = progressBarReporter;
        }

        public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent)
        {
        }

        public void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested)
        {
            _progressBarReporter.ReportInitialState(mutantsToBeTested.Count());
        }

        public void OnMutantTested(IReadOnlyMutant result)
        {
            _progressBarReporter.ReportRunTest(result);
        }

        public void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, TestProjectsInfo testProjectsInfo = null)
        {
            _progressBarReporter.ReportFinalState();
        }
    }
}
