extern alias r4b;

using Hl7.Fhir.Model;

namespace UploadFIG.Test
{
    [TestClass]
    public class CheckImplementationGuides
    {
		[TestMethod]
		public async Task FMG_Review()
		{
			// "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
			string outputFile = "c:\\temp\\uploadfig-dump-fmg.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-odf", outputFile,
                // "--verbose",
				// "-s", "https://build.fhir.org/ig/HL7/cqf-measures/package.tgz",
                
                "-s", "https://build.fhir.org/ig/HL7/cqf-recommendations/package.tgz",
			});
			Assert.AreEqual(0, result);

			string json = System.IO.File.ReadAllText(outputFile);
			var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
			Bundle bun = new Bundle();

			Console.WriteLine();
			Console.WriteLine("--------------------------------------");
			Console.WriteLine("Recursively Scanning Dependencies...");
			PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);

			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}

		[TestMethod]
		public async Task CheckFhirCoreR4()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-r4core.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.r4.core",
				"-odf", outputFile,
			});
			Assert.AreEqual(0, result);

			Assert.AreEqual(4518, Program.successes);
			Assert.AreEqual(10, Program.failures);
			Assert.AreEqual(66, Program.validationErrors);
		}

		[TestMethod]
		public async Task CheckFhirExamplesR4()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-r4examples.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.r4.examples",
				"-odf", outputFile,
			});
			Assert.AreEqual(0, result);

			Assert.AreEqual(4546, Program.successes);
			Assert.AreEqual(11, Program.failures);
			Assert.AreEqual(66, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDC300()
        {
            // "commandLineArgs": "-t -pid hl7.fhir.uv.sdc"
            string outputFile = "c:\\temp\\uploadfig-dump-sdc.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-vq",
                "--includeExamples",
				"-pid", "hl7.fhir.uv.sdc",
				"-pv", "3.0.0",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(125, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(15, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDC_CI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-sdc-ci.json";
            var result = await Program.Main(new[]
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
            Assert.AreEqual(0, result);

			Assert.AreEqual(161, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(4, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckUsCore311()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 3.1.1 -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore311.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
                "-pv", "3.1.1",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(142, Program.successes);
			Assert.AreEqual(1, Program.failures);
			Assert.AreEqual(3, Program.validationErrors);
        }

        [TestMethod]
        public async Task CheckUsCoreLatest()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 6.0.0-ballot -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(209, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(6, Program.validationErrors);
        }


        [TestMethod]
		public async Task CheckExtensionsRelease()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-extensions.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "https://hl7.org/fhir/extensions/package.tgz",
				"-odf", outputFile,
			});
			Assert.AreEqual(0, result);
		}

		[TestMethod]
		public async Task CheckExtensionsCiR4()
		{
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				// "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/hl7.fhir.uv.extensions.r4.tgz",
				"-s", @"C:\Users\brianpo\Downloads\hl7.fhir.uv.extensions.r4 (3).tgz"
			});
			Assert.AreEqual(0, result);
		}

		[TestMethod]
		public async Task CheckExtensionsCiR4B()
		{
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				// "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/hl7.fhir.uv.extensions.r4b.tgz",
				"-s", @"C:\Users\brianpo\Downloads\hl7.fhir.uv.extensions.r4b (2).tgz"
			});
			Assert.AreEqual(0, result);
		}

		[TestMethod]
        public async Task CheckExtensionsCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-extensions.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				// "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/package.tgz",
				"-s", "C:/git/hl7/fhir-extensions/output/package.tgz",
				"-odf", outputFile,
            });
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public async Task CheckAuSmartFormsCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-au-smartforms.json";
            var result = await Program.Main(new[]
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
            Assert.AreEqual(0, result);

			Assert.AreEqual(54, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckUsCoreCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/HL7/US-Core/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(215, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(1, Program.validationErrors);
		}

		[TestMethod]
		public async Task CheckUsCore610()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.core",
				"-pv", "6.1.0",
				"-odf", outputFile,
			});
			Assert.AreEqual(0, result);

			Assert.AreEqual(209, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(6, Program.validationErrors);
		}

		[TestMethod]
		public async Task CheckUsNDH_CI()
		{
			string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"-s", "http://build.fhir.org/ig/HL7/fhir-us-ndh/package.tgz",
				"-odf", outputFile,
			});
			Assert.AreEqual(0, result);

			Assert.AreEqual(238, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(1, Program.validationErrors); // the search parameter 'special' type creates an info message
		}

		[TestMethod]
        public async Task CheckMcode()
        {
            // "commandLineArgs": "-t -pid hl7.fhir.us.mcode -odf c:/temp/uploadfig-dump.json"
            string outputFile = "c:\\temp\\uploadfig-dump-mcode.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.mcode",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(103, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(1, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuCore()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
            var result = await Program.Main(new[] {
                "-t",
				"-vq",
				"-reg", "https://api.healthterminologies.gov.au/integration/R4/fhir",
				"--includeReferencedDependencies",
				"--includeExamples",
				"-pid", "hl7.fhir.au.core",
				// "-pcv",
				"-sn",
                "-odf", outputFile,
				"-ocb", "c:\\temp\\uploadfig-dump-aucore-bundle-raw.json",
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(234, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuCoreCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeReferencedDependencies",
				"--includeExamples",
				"-s", "https://build.fhir.org/ig/hl7au/au-fhir-core/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(26, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckAuBase()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var result = await Program.Main(new[]
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
            Assert.AreEqual(0, result);

			Assert.AreEqual(138, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
        }

        [TestMethod]
        public async Task CheckAuBaseCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.au.base",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(138, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
        }

        [TestMethod]
        public async Task CheckSDOC_ClinicalCare()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-sdoh-clinicalcare.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-pid", "hl7.fhir.us.sdoh-clinicalcare",
                "-odf", outputFile,
                // "--verbose",
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(42, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(3, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckSDOC_ClinicalCareCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-sdoh-clinicalcare.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
                // "--verbose",
                // "-s", "https://build.fhir.org/ig/HL7/fhir-sdoh-clinicalcare/package.tgz",
				"-s", "C:\\git\\hl7\\fhir-sdoh-clinicalcare\\output/package.tgz",
				"-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(42, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(1, Program.validationErrors);
		}


		[TestMethod]
        public async Task CheckSubsBackportCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-subs-backportCI.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
                // "--verbose",
                "-s", "http://build.fhir.org/ig/HL7/fhir-subscription-backport-ig/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(20, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(1, Program.validationErrors);
		}

		[TestMethod]
        public async Task CheckIHE_MHD()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-ihe-mhd.json";
            var result = await Program.Main(new[]
            {
                "-t",
				"-vq",
				"--includeExamples",
				"-s", "https://profiles.ihe.net/ITI/MHD/4.2.1/package.tgz",
                "-odf", outputFile,
                // "-fd", "false"
                // "-sf", "package/StructureDefinition-IHE.MHD.EntryUUID.Identifier.json",
            });
            Assert.AreEqual(0, result);

			Assert.AreEqual(49, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(2, Program.validationErrors);
        }

		[TestMethod]
		public async Task CheckDavinciRA()
		{
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"--includeReferencedDependencies",
				"-s", "https://build.fhir.org/ig/HL7/davinci-ra/branches/master/package.tgz",
                // "-fd", "false"
				"-ocb", @"c:\temp\UploadFIG-dump-DavinciRA-bundle.json",
				"-pcv",
            });
			Assert.AreEqual(0, result);

			Assert.AreEqual(55, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}

		[TestMethod]
		public async Task CheckDavinciCRD()
		{
			var result = await Program.Main(new[]
			{
				"-t",
				"-vq",
				"--includeExamples",
				"--includeReferencedDependencies",
				"-sn",
				"-pcv",
				"-s", "https://build.fhir.org/ig/HL7/davinci-crd/branches/master/package.tgz",
                // "-fd", "false"
				"-ocb", @"c:\temp\uploadfig-dump-daviniCRD-bundle.json",
            });
			Assert.AreEqual(0, result);

			Assert.AreEqual(226, Program.successes);
			Assert.AreEqual(0, Program.failures);
			Assert.AreEqual(0, Program.validationErrors);
		}
	}
}