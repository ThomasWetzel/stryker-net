using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.FSharp.Collections;
using Stryker.Core.CoverageAnalysis;
using Stryker.Core.Exceptions;
using Stryker.Core.Initialisation;
using Stryker.Core.Initialisation.Buildalyzer;
using Stryker.Core.Logging;
using Stryker.Core.MutantFilters;
using Stryker.Core.Mutants;
using Stryker.Core.Options;
using Stryker.Core.ProjectComponents;
using Stryker.Core.Reporters;
using static FSharp.Compiler.SyntaxTree;

namespace Stryker.Core.MutationTest
{
    public class MutationTestProcess : IMutationTestProcess
    {
        public MutationTestInput Input { get; }
        private readonly IProjectComponent _projectContents;
        private readonly ILogger _logger;
        private readonly IMutationTestExecutor _mutationTestExecutor;
        private readonly IFileSystem _fileSystem;
        private readonly BaseMutantOrchestrator _orchestrator;
        private readonly IReporter _reporter;
        private readonly ICoverageAnalyser _coverageAnalyser;
        private readonly StrykerOptions _options;
        private readonly Language _language;
        private IMutationProcess _mutationProcess;

        public MutationTestProcess(MutationTestInput mutationTestInput,
            IReporter reporter,
            IMutationTestExecutor mutationTestExecutor,
            BaseMutantOrchestrator<SyntaxNode> cSharpOrchestrator = null,
            BaseMutantOrchestrator<FSharpList<SynModuleOrNamespace>> fSharpOrchestrator = null,
            IFileSystem fileSystem = null,
            IMutantFilter mutantFilter = null,
            ICoverageAnalyser coverageAnalyser = null,
            StrykerOptions options = null)
        {
            Input = mutationTestInput;
            _projectContents = mutationTestInput.ProjectInfo.ProjectContents;
            _reporter = reporter;
            _options = options;
            _mutationTestExecutor = mutationTestExecutor;
            _fileSystem = fileSystem ?? new FileSystem();
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<MutationTestProcess>();
            _coverageAnalyser = coverageAnalyser ?? new CoverageAnalyser(_options, _mutationTestExecutor, Input);
            _language = Input.ProjectInfo.ProjectUnderTestAnalyzerResult.GetLanguage();
            _orchestrator = cSharpOrchestrator ?? fSharpOrchestrator ?? ChooseOrchestrator(_options);

            SetupMutationTestProcess(mutantFilter);
        }

        private BaseMutantOrchestrator ChooseOrchestrator(StrykerOptions options)
        {
            if (_language == Language.Fsharp)
            {
                return new FsharpMutantOrchestrator(options: options);
            }

            return new CsharpMutantOrchestrator(options: options);
        }

        private void SetupMutationTestProcess(IMutantFilter mutantFilter)
        {

            if (_language == Language.Csharp)
            {
                _mutationProcess = new CsharpMutationProcess(Input, _fileSystem, _options, mutantFilter, (BaseMutantOrchestrator<SyntaxNode>)_orchestrator);
            }
            else if (_language == Language.Fsharp)
            {
                _mutationProcess = new FsharpMutationProcess(Input, (BaseMutantOrchestrator<FSharpList<SynModuleOrNamespace>>)_orchestrator, _fileSystem, _options);
            }
            else
            {
                throw new GeneralStrykerException("no valid language detected || no valid csproj or fsproj was given");
            }
        }

        public void Mutate()
        {
            Input.ProjectInfo.BackupOriginalAssembly();
            _mutationProcess.Mutate();
        }

        public void FilterMutants() => _mutationProcess.FilterMutants();

        public StrykerRunResult Test(IEnumerable<Mutant> mutantsToTest)
        {
            if (!MutantsToTest(mutantsToTest))
            {
                return new StrykerRunResult(_options, double.NaN);
            }

            var mutantCount = mutantsToTest.Count();
            var buildMutantGroupsForTest = BuildMutantGroupsForTest(mutantsToTest.ToList()).ToList();
            _logger.LogDebug(
                $"Mutations will be tested in {buildMutantGroupsForTest.Count} test runs" +
                (mutantCount > buildMutantGroupsForTest.Count ? $", instead of {mutantCount}." : "."));

            TestMutants(buildMutantGroupsForTest);

            return new StrykerRunResult(_options, _projectContents.ToReadOnlyInputComponent().GetMutationScore());
        }

        public IEnumerable<string> GetTestNames(ITestListDescription testList) => _mutationTestExecutor.TestRunner.DiscoverTests().Extract(testList.GetGuids()).Select(t => t.Name);

        public MutantDiagnostic DiagnoseMutant(IList<Mutant> mutants, int mutantToDiagnose)
        {
            var monitoredMutant = Input.ProjectInfo.ProjectContents.Mutants.First(m => m.Id == mutantToDiagnose);
            _logger.LogWarning($"Diagnosing mutant {mutantToDiagnose}.");
            if (monitoredMutant.CoveringTests.IsEveryTest)
            {
                _logger.LogWarning("This mutant is run against every test, unable to automatically diagnose issue.");
                return null;
            }
            _logger.LogInformation("Mutant is covered by the following tests: ");
            var result = new MutantDiagnostic(GetTestNames(monitoredMutant.CoveringTests));
            _logger.LogInformation(string.Join(',', result.CoveringTests));
            
            _logger.LogInformation("*** Step 1 normal run ***");
            var step = PerformStep(mutants, monitoredMutant);
            result.DeclareResult(step.status, GetTestNames(step.killingTests));
            // clean up status
            _logger.LogInformation("*** Step 2 solo run ***");
            step = PerformStep(new List<Mutant> { monitoredMutant }, monitoredMutant);
            result.DeclareResult(step.status, GetTestNames(step.killingTests));
            // clean up status
            _logger.LogInformation("*** Step 3 run against all tests ***");
            monitoredMutant.CoveringTests = TestsGuidList.EveryTest();
            step = PerformStep(new List<Mutant> { monitoredMutant }, monitoredMutant);
            result.DeclareResult(step.status, GetTestNames(step.killingTests));
            _logger.LogInformation("*** Step 3 solo run against all ***");
            return result;
        }

        private (MutantStatus status, ITestListDescription killingTests) PerformStep(IEnumerable<Mutant> mutants, Mutant monitoredMutant)
        {
            monitoredMutant.ResultStatus = MutantStatus.NotRun;
            var groups = BuildMutantGroupsForTest(mutants.ToList()).Where(l => l.Contains(monitoredMutant)).ToList();
            TestMutants(groups);

            var step2 = monitoredMutant.ResultStatus;
            _logger.LogInformation($"Mutant {monitoredMutant.Id} is {step2}.");
            return (step2, monitoredMutant.KillingTests);
        }

        public void Restore() => Input.ProjectInfo.RestoreOriginalAssembly();

        private void TestMutants(IEnumerable<List<Mutant>> mutantGroups)
        {
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _options.Concurrency };

            var testsFailingInitially = Input.InitialTestRun.Result.FailingTests.GetGuids().ToHashSet();

            Parallel.ForEach(mutantGroups, parallelOptions, mutants =>
            {
                var reportedMutants = new HashSet<Mutant>();

                bool TestUpdateHandler(IReadOnlyList<Mutant> testedMutants, ITestGuids failedTests, ITestGuids ranTests, ITestGuids timedOutTest)
                {
                    var continueTestRun = _options.OptimizationMode.HasFlag(OptimizationModes.DisableBail);
                    if (testsFailingInitially.Count > 0 && failedTests.GetGuids().Any(id => testsFailingInitially.Contains(id)))
                    {
                        // some of the failing tests where failing without any mutation
                        // we discard those tests
                        failedTests = new TestsGuidList(
                            failedTests.GetGuids().Where(t => !testsFailingInitially.Contains(t)));
                    }
                    foreach (var mutant in testedMutants)
                    {
                        mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTest);

                        if (mutant.ResultStatus == MutantStatus.NotRun)
                        {
                            continueTestRun = true; // Not all mutants in this group were tested so we continue
                        }

                        OnMutantTested(mutant, reportedMutants); // Report on mutant that has been tested
                    }

                    return continueTestRun;
                }
                _mutationTestExecutor.Test(mutants, Input.InitialTestRun.TimeoutValueCalculator, TestUpdateHandler);

                OnMutantsTested(mutants, reportedMutants);
            });
        }

        private void OnMutantsTested(IEnumerable<Mutant> mutants, ISet<Mutant> reportedMutants)
        {
            foreach (var mutant in mutants)
            {
                if (mutant.ResultStatus == MutantStatus.NotRun)
                {
                    _logger.LogWarning($"Mutation {mutant.Id} was not fully tested.");
                }

                OnMutantTested(mutant, reportedMutants);
            }
        }

        private void OnMutantTested(Mutant mutant, ISet<Mutant> reportedMutants)
        {
            if (mutant.ResultStatus != MutantStatus.NotRun && reportedMutants.Add(mutant))
            {
                _reporter?.OnMutantTested(mutant);
            }
        }

        private bool MutantsToTest(IEnumerable<Mutant> mutantsToTest)
        {
            if (!mutantsToTest.Any())
            {
                return false;
            }
            if (mutantsToTest.Any(x => x.ResultStatus != MutantStatus.NotRun))
            {
                throw new GeneralStrykerException("Only mutants to run should be passed to the mutation test process. If you see this message please report an issue.");
            }

            return true;
        }

        private IEnumerable<List<Mutant>> BuildMutantGroupsForTest(IReadOnlyCollection<Mutant> mutantsNotRun)
        {

            if (_options.OptimizationMode.HasFlag(OptimizationModes.DisableMixMutants) || !_options.OptimizationMode.HasFlag(OptimizationModes.CoverageBasedTest))
            {
                return mutantsNotRun.Select(x => new List<Mutant> { x });
            }

            var blocks = new List<List<Mutant>>(mutantsNotRun.Count);
            var mutantsToGroup = mutantsNotRun.ToList();
            // we deal with mutants needing full testing first
            blocks.AddRange(mutantsToGroup.Where(m => m.MustRunAgainstAllTests).Select(m => new List<Mutant> { m }));
            mutantsToGroup.RemoveAll(m => m.MustRunAgainstAllTests);

            var testsCount = mutantsToGroup.Sum(m => m.CoveringTests.Count);
            mutantsToGroup = mutantsToGroup.OrderBy(m => m.CoveringTests.Count).ToList();
            for (var i = 0; i < mutantsToGroup.Count; i++)
            {
                var usedTests = mutantsToGroup[i].CoveringTests;
                var nextBlock = new List<Mutant> { mutantsToGroup[i] };
                for (var j = i + 1; j < mutantsToGroup.Count; j++)
                {
                    var currentMutant = mutantsToGroup[j];
                    var nextSet = currentMutant.CoveringTests;
                    if (nextSet.Count + usedTests.Count > testsCount ||
                        nextSet.ContainsAny(usedTests))
                    {
                        continue;
                    }
                    // add this mutant to the block
                    nextBlock.Add(currentMutant);
                    // remove the mutant from the list of mutants to group
                    mutantsToGroup.RemoveAt(j--);
                    // add this mutant tests
                    usedTests = usedTests.Merge(nextSet);
                }

                blocks.Add(nextBlock);
            }

            return blocks;
        }

        public void GetCoverage() => _coverageAnalyser.DetermineTestCoverage();
    }
}
