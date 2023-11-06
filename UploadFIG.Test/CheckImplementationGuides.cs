extern alias r4b;

using Hl7.Fhir.Model;

namespace UploadFIG.Test
{
    [TestClass]
    public class CheckImplementationGuides
    {
        [TestMethod]
        public async Task CheckSDC()
        {
            // "commandLineArgs": "-t -pid hl7.fhir.uv.sdc"
            string outputFile = "c:\\temp\\uploadfig-dump-sdc.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.uv.sdc",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
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
                "-s", "https://build.fhir.org/ig/HL7/sdc/package.tgz",
                "-odf", outputFile,
                "--verbose",
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckUsCore311()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 3.1.1 -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore311.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.us.core",
                "-pv", "3.1.1",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckUsCoreLatest()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.core -pv 6.0.0-ballot -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.us.core",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }


        [TestMethod]
        public async Task CheckExtensionsCI()
        {
            // var result = await Program.Main(new[] { "-t", "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/branches/2023-10-gg-qa/package.tgz", "-odf", "c:\\temp\\uploadfig-dump-extensions.json" });
            string outputFile = "c:\\temp\\uploadfig-dump-extensions.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-s", "https://build.fhir.org/ig/HL7/fhir-extensions/package.tgz",
                "-odf", outputFile,
                "-sf", "package/SearchParameter-valueset-extensions-ValueSet-end.json",
                "--verbose",
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
                "-r", "Questionnaire",
                "-r", "ValueSet",
                "-r", "CodeSystem",
                "-s", "https://build.fhir.org/ig/aehrc/smart-forms-ig/branches/master/package.tgz",
                "-odf", outputFile,
                // "-sf", "package/SearchParameter-valueset-extensions-ValueSet-end.json",
                "--verbose",
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckUsCoreCI()
        {
            string outputFile = "c:\\temp\\uploadfig-dump-uscore.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-s", "https://build.fhir.org/ig/HL7/US-Core/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckMcode()
        {
            // "commandLineArgs": "-t -pid hl7.fhir.us.mcode -odf c:/temp/uploadfig-dump.json"
            string outputFile = "c:\\temp\\uploadfig-dump-mcode.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.us.mcode",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckAuCore()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
            var result = await Program.Main(new[] {
                "-t",
                "-pid", "hl7.fhir.au.core",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckAuCoreCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.core -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aucore.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.au.core",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckAuBase()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.au.base",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckAuBaseCI()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.au.base -fd -pdv false"
            string outputFile = "c:\\temp\\uploadfig-dump-aubase.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-pid", "hl7.fhir.au.base",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckSDOC_ClinicalCare()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-sdoh-clinicalcare.json";
            var result = await Program.Main(new[]
            { "-t",
                "-pid", "hl7.fhir.us.sdoh-clinicalcare",
                "-odf", outputFile,
                "--verbose",
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
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
                "--verbose",
                "-s", "https://build.fhir.org/ig/HL7/fhir-sdoh-clinicalcare/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
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
                "--verbose",
                "-s", "http://build.fhir.org/ig/HL7/fhir-subscription-backport-ig/package.tgz",
                "-odf", outputFile,
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }

        [TestMethod]
        public async Task CheckIHE_MHD()
        {
            // "commandLineArgs": "-d https://localhost:44391 -pid hl7.fhir.us.sdoh-clinicalcare -fd -pdv false --verbose"
            string outputFile = "c:\\temp\\uploadfig-dump-ihe-mhd.json";
            var result = await Program.Main(new[]
            {
                "-t",
                "-s", "https://profiles.ihe.net/ITI/MHD/4.2.1/package.tgz",
                "-odf", outputFile,
                "-sf", "package/StructureDefinition-IHE.MHD.EntryUUID.Identifier.json",
            });
            Assert.AreEqual(0, result);

            string json = System.IO.File.ReadAllText(outputFile);
            var output = System.Text.Json.JsonSerializer.Deserialize<OutputDependenciesFile>(json);
            Bundle bun = new Bundle();

            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("Recursively Scanning Dependencies...");
            PrepareDependantPackage.RecursivelyScanPackageForCanonicals(output, bun);
        }
    }
}