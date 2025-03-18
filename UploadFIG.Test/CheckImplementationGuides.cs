extern alias r4b;

using System.CommandLine.NamingConventionBinder;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Newtonsoft.Json;

namespace UploadFIG.Test
{
    [TestClass]
    public class CheckImplementationGuides
    {
        private string _cacheFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UploadFIG", "PackageCache");

        private StringBuilder _sb;
        private TextWriter _writer;
        private TextWriter _rawWriter;

        [TestInitialize()]
        public void Initialize()
        {
            // Grab the console output from the test into a StringBuilder
            _sb = new();
            _writer = new StringWriter(_sb);
            _rawWriter = Console.Out;
            Console.SetOut(_writer);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Put the captured console out to the original console stream
            Console.SetOut(_rawWriter);
            Console.WriteLine(_sb);
        }

        public async Task CheckTestResults(string testName, Program.Result results)
        {
            string testResultPath = Path.Combine(System.IO.Path.GetTempPath(), "UploadFIG", "TestResult", testName);

            // Check the console output
            if (File.Exists(testResultPath + ".txt"))
            {
                // this times results
                System.IO.File.WriteAllText(testResultPath + "2.txt", _sb.ToString());

                var expectedResult = File.ReadAllText(testResultPath + ".txt");
                Assert.AreEqual(expectedResult, _sb.ToString());
            }
            else
            {
                System.IO.File.WriteAllText(testResultPath + ".txt", _sb.ToString());
            }

            // Check the dependencies file
            JsonSerializerSettings serializerSettings = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };
            if (File.Exists(testResultPath + "_deps.json"))
            {
                // this times results
                System.IO.File.WriteAllText(testResultPath + "_deps2.json", JsonConvert.SerializeObject(results.OutputDependencies, serializerSettings));

                var expectedDeps = File.ReadAllText(testResultPath + "_deps.json");
                OutputDependenciesFile expectedItems = JsonConvert.DeserializeObject<OutputDependenciesFile>(expectedDeps, serializerSettings);
                // Assert.AreEqual(expectedDeps, _sb.ToString());
                var expectedUrls = expectedItems.containedCanonicals.Select(cc => $"{cc.Canonical}|{cc.Version}");
                var actualUrls = results.OutputDependencies.containedCanonicals.Select(cc => $"{cc.Canonical}|{cc.Version}");
                Assert.AreEqual(string.Empty, string.Join(",\n", expectedUrls.Where(ec => !actualUrls.Contains(ec))), "Missing URLs");
                Assert.AreEqual(string.Empty, string.Join(",\n", actualUrls.Where(ec => !expectedUrls.Contains(ec))), "Extra URLs");
            }
            else
            {
                System.IO.File.WriteAllText(testResultPath + "_deps.json", JsonConvert.SerializeObject(results.OutputDependencies, serializerSettings));
            }

            // Check the output bundle
            if (File.Exists(testResultPath + "_bundle.json"))
            {
                // This times results
                var fs = new FileStream(testResultPath + "_bundle2.json", FileMode.Create);
                using (fs)
                {
                    await results.Processor.SerializeJson(fs, results.AlternativeOutputBundle);
                }

                var expectedBundleJson = File.ReadAllText(testResultPath + "_bundle.json");
                var bundle = results.Processor.ParseJson(expectedBundleJson) as Bundle;
                Assert.AreEqual(bundle.Total, results.AlternativeOutputBundle.Total);
                // now do more comparisons
            }
            else
            {
                var fs = new FileStream(testResultPath + "_bundle.json", FileMode.Create);
                using (fs)
                {
                    await results.Processor.SerializeJson(fs, results.AlternativeOutputBundle);
                }
            }
        }

        public static Settings ParseArguments(string[] args)
        {
            Settings settings = null;
            RootCommand rootCommand = Program.GetRootCommand(args);
            rootCommand.Handler = CommandHandler.Create((Settings context) =>
            {
                settings = context;
                return 0;
            });
            rootCommand.Invoke(args);
            System.Diagnostics.Trace.WriteLine("```");
            System.Diagnostics.Trace.WriteLine($"> UploadFIG {string.Join(" ", args)}");
            System.Diagnostics.Trace.WriteLine("```");
            return settings;
        }

		[TestMethod]
		public async Task FMG_Review()
		{
			// "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
			string outputFile = "c:\\temp\\uploadfig-dump-fmg.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-odf", outputFile,
                // "--verbose",
				// "-s", "https://build.fhir.org/ig/HL7/cqf-measures/package.tgz",
                
                "-s", "https://build.fhir.org/ig/HL7/cqf-recommendations/package.tgz",
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}

		[TestMethod]
		public async Task CheckFhirCoreR4()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-r4core.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				// "--includeExamples",
				"-pid", "hl7.fhir.r4.core",
				"-odf", outputFile,
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(4518, result.successes);
			Assert.AreEqual(10, result.failures);
			Assert.AreEqual(69, result.validationErrors);
		}

		[TestMethod]
		public async Task CheckFhirExamplesR4()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-r4examples.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.r4.examples",
				"-odf", outputFile,
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(4546, result.successes);
			Assert.AreEqual(11, result.failures);
			Assert.AreEqual(69, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDC300()
        {
            var settings = new Settings
            {
                TestPackageOnly = true,
                ValidateQuestionnaires = true,
                IncludeExamples = true,
                SourcePackagePath = _cacheFolder + "/hl7.fhir.uv.sdc_3_0_0.tgz",
                ResourceTypes = Program.defaultResourceTypes.ToList(),
            };
            var result = await Program.UploadPackageInternal(settings);

            Assert.AreEqual(0, result.Value);

            await CheckTestResults("sdc300", result);

			Assert.AreEqual(125, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(15, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDC_CI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-sdc-ci.json";
            var settings = ParseArguments(new[]
            {
                "-t",
                "-vq",
                "--includeExamples",
				"--includeReferencedDependencies",
				"-s", "https://build.fhir.org/ig/HL7/sdc/package.tgz",
				// "-s", @"c:\git\hl7\sdc\output\package.tgz",
				"-odf", outputFile,
                // "--verbose",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(161, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(4, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckUsCore311()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 3.1.1 -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore311.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
                "-pv", "3.1.1",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(142, result.successes);
			Assert.AreEqual(1, result.failures);
			Assert.AreEqual(3, result.validationErrors);
        }

        [TestMethod]
        public async Task CheckUsCoreLatest()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 6.0.0-ballot -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(209, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(6, result.validationErrors);
        }


        [TestMethod]
		public async Task CheckExtensionsRelease()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-extensions.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "https://hl7.org/fhir/extensions/package.tgz",
				"-odf", outputFile,
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);
		}

		[TestMethod]
		public async Task CheckExtensionsCiR4()
		{
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/HL7/fhir-extensions/hl7.fhir.uv.extensions.r4.tgz",
				// "-s", @"C:\Users\brianpo\Downloads\hl7.fhir.uv.extensions.r4 (3).tgz"
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);
		}

		[TestMethod]
		public async Task CheckExtensionsCiR4B()
		{
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/HL7/fhir-extensions/branches/2025-03-gg-r4b/hl7.fhir.uv.extensions.r4b.tgz",
				// "-s", @"C:\Users\brianpo\Downloads\hl7.fhir.uv.extensions.r4b (2).tgz"
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);
		}

		[TestMethod]
        public async Task CheckExtensionsCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-extensions.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				// "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/package.tgz",
				"-s", "C:/git/hl7/fhir-extensions/output/package.tgz",
				"-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);
        }

        [TestMethod]
        public async Task CheckAuSmartFormsCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-au-smartforms.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-r", "Questionnaire",
                "-r", "ValueSet",
                "-r", "CodeSystem",
                "-s", "https://build.fhir.org/ig/aehrc/smart-forms-ig/branches/master/package.tgz",
                "-odf", outputFile,
                // "-sf", "package/SearchParameter-valueset-extensions-ValueSet-end.json",
                // "--verbose",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(54, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckUsCoreCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/HL7/US-Core/package.tgz",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(215, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(1, result.validationErrors);
		}

		[TestMethod]
		public async Task CheckUsCore610()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
				"-pv", "6.1.0",
				"-odf", outputFile,
			});
            var result = await Program.UploadPackageInternal(settings);

            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(268, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(6, result.validationErrors);
		}

		[TestMethod]
		public async Task CheckUsNDH_CI()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "http://build.fhir.org/ig/HL7/fhir-us-ndh/package.tgz",
				"-odf", outputFile,
			});
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(226, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(3, result.validationErrors); // the search parameter 'special' type creates an info message
		}

		[TestMethod]
        public async Task CheckMcode()
        {
            // "commandLineArgs": "-t -pid hl7.fhir.us.mcode -odf c:/temp/uploadfig-dump.json"
            string outputFile = "c:\\temp\\uploadfig-dump-mcode.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.mcode",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(103, Program.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuCore()
        {
            var settings = new Settings
            {
                TestPackageOnly = true,
                ValidateQuestionnaires = true,
                IncludeExamples = true,
                IncludeReferencedDependencies = true,
                ValidateReferencedDependencies = true,
                SourcePackagePath = _cacheFolder + "/hl7.fhir.au.core_1_0_0-preview.tgz",
                StripNarratives = true,
                // PatchCanonicalVersions = true,
                ResourceTypes = Program.defaultResourceTypes.ToList(),
                // SelectFiles = ["package/StructureDefinition-au-core-patient.json"],
            };
            var result = await Program.UploadPackageInternal(settings);

            Assert.AreEqual(0, result.Value);

            await CheckTestResults("aucore100", result);

   //         // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
   //         string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
   //         var result = await Program.Main(new[] {
   //             "-t",
			//	"-vq",
			//	"-reg", "https://api.healthterminologies.gov.au/integration/R4/fhir",
			//	"--includeReferencedDependencies",
			//	"--includeExamples",
			//	"-pid", "hl7.fhir.au.core",
			//	"-pv", "1.0.0",
			//	// "-pcv",
			//	"-sn",
   //             "-odf", outputFile,
			//	"-of", "c:\\temp\\uploadfig-dump-aucore-bundle-raw.json",
			//});
   //         Assert.AreEqual(0, result);

			Assert.AreEqual(132, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(14, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuCoreCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeReferencedDependencies",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/hl7au/au-fhir-core/package.tgz",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(26, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuBase()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.au.base",
				"-reg", "https://api.healthterminologies.gov.au/integration/R4/fhir",
				"-rego", "c:\\temp\\au-registry-content.json",
				"-ets", "https://tx.dev.hl7.org.au/fhir",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(138, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
        }

        [TestMethod]
        public async Task CheckAuBaseCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.au.base",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(138, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
        }

        [TestMethod]
        public async Task CheckSDOC_ClinicalCare()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-sdoh-clinicalcare.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.sdoh-clinicalcare",
                "-odf", outputFile,
                // "--verbose",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(42, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(3, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDOC_ClinicalCareCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-sdoh-clinicalcare.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
                // "--verbose",
                // "-s", "https://build.fhir.org/ig/HL7/fhir-sdoh-clinicalcare/package.tgz",
				"-s", "C:\\git\\hl7\\fhir-sdoh-clinicalcare\\output/package.tgz",
				"-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(42, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(1, result.validationErrors);
		}


		[TestMethod]
        public async Task CheckSubsBackportCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-subs-backportCI.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
                // "--verbose",
                "-s", "http://build.fhir.org/ig/HL7/fhir-subscription-backport-ig/package.tgz",
                "-odf", outputFile,
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(20, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(1, result.validationErrors);
		}

		[TestMethod]
        public async Task CheckIHE_MHD()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-ihe-mhd.json";
            var settings = ParseArguments(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-s", "https://profiles.ihe.net/ITI/MHD/4.2.1/package.tgz",
                "-odf", outputFile,
                // "-fd", "false"
                // "-sf", "package/StructureDefinition-IHE.MHD.EntryUUID.Identifier.json",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(49, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(2, result.validationErrors);
        }

		[TestMethod]
		public async Task CheckDavinciRA()
		{
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"--includeReferencedDependencies",
				"-s", "https://build.fhir.org/ig/HL7/davinci-ra/branches/master/package.tgz",
                // "-fd", "false"
				"-of", @"c:\temp\UploadFIG-dump-DavinciRA-bundle.json",
				"-pcv",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(55, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}

		[TestMethod]
		public async Task CheckDavinciCRD()
		{
			var settings = ParseArguments(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"--includeReferencedDependencies",
				"--validateReferencedDependencies",
                // "-ip", "hl7.fhir.us.core|7.0.0",
                // "-ip", "hl7.fhir.us.core|3.1.1",
                // "-ip", "hl7.terminology.r4|5.0.0", // hl7.terminology.r4|6.2.0 is already included, so let all the terminologies use that instead
                // "-ip", "us.nlm.vsac|0.11.0", // us.nlm.vsac|0.19.0 is the preferred one for this IG, so skip the older one.
                // "-ip", "hl7.fhir.uv.extensions.r4|1.0.0", // hl7.fhir.uv.extensions.r4|5.2.0 is the preferred one for this IG, so skip the older one.
                // "-ip", "hl7.fhir.uv.extensions.r4|5.1.0", // hl7.fhir.uv.extensions.r4|5.2.0 is the preferred one for this IG, so skip the older one.
				"-sn",
				"-pcv",
				"-s", "https://build.fhir.org/ig/HL7/davinci-crd/branches/master/package.tgz",
                // "-fd", "false"
				"-of", @"c:\temp\uploadfig-dump-daviniCRD-bundle.json",
            });
            var result = await Program.UploadPackageInternal(settings);
            Assert.AreEqual(0, result.Value);

			Assert.AreEqual(226, result.successes);
			Assert.AreEqual(0, result.failures);
			Assert.AreEqual(0, result.validationErrors);
		}
	}
}
