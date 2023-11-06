// See https://aka.ms/new-console-template for more information
// using AngleSharp;
using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using UploadFIG.Helpers;

namespace UploadFIG
{
    public class Program
    {
        /// <summary>Main entry-point for this application.</summary>
        /// <param name="args">An array of command-line argument strings.</param>
        public static async Task<int> Main(string[] args)
        {
            // setup our configuration (command line > environment > appsettings.json)
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            var settings = configuration.Get<Settings>();

            // Provide defaults if not provided
            if (settings.ResourceTypes?.Any() != true)
                settings.ResourceTypes = new[] {
                    "StructureDefinition",
                    "ValueSet",
                    "CodeSystem",
                    "Questionnaire",
                    "SearchParameter",
                    "ConceptMap",
                    "StructureMap",
                    "Library",
                }.ToList();


            Console.WriteLine("HL7 FHIR Implementation Guide Uploader");
            Console.WriteLine("--------------------------------------");

            var rootCommand = new RootCommand("HL7 FHIR Implementation Guide Uploader")
            {
                // Mandatory parameters
                new Option<string>(new string[]{ "-s", "--sourcePackagePath"}, () => settings.SourcePackagePath, "The explicit path of a package to process (over-rides PackageId/Version)"),
                new Option<string>(new string[]{ "-pid", "--packageId"}, () => settings.PackageId, "The Package ID of the package to upload (from the HL7 FHIR Package Registry)"),

                // Optional parameters
                new Option<bool>(new string[]{ "-fd", "--forceDownload"}, () => settings.ForceDownload, "Force the download of the package from the source package path\r\n(If not specified, will use the last downloaded package)"),
                new Option<string>(new string[]{ "-pv", "--packageVersion"}, () => settings.PackageVersion, "The version of the Package to upload (from the HL7 FHIR Package Registry)"),
                new Option<List<string>>(new string[]{ "-r", "--resourceTypes"}, () => settings.ResourceTypes, "Which resource types should be processed by the uploader"),
                new Option<List<string>>(new string[]{ "-sf", "--selectFiles"}, () => settings.SelectFiles, "Only process these selected files\r\n(e.g. package/SearchParameter-valueset-extensions-ValueSet-end.json)"),
                new Option<List<string>>(new string[]{ "-if", "--ignoreFiles" }, () => settings.IgnoreFiles, "Any specific files that should be ignored/skipped when processing the package"),
                new Option<List<string>>(new string[]{ "-ic", "--ignoreCanonicals" }, () => settings.IgnoreCanonicals, "Any specific Canonical URls that should be ignored/skipped when processing the package"),
                new Option<string>(new string[]{ "-d", "--destinationServerAddress" }, () => settings.DestinationServerAddress, "The URL of the FHIR Server to upload the package contents to"),
                new Option<List<string>>(new string[]{ "-dh", "--destinationServerHeaders"}, () => settings.DestinationServerHeaders, "Headers to add to the request to the destination FHIR Server"),
                new Option<upload_format>(new string[]{ "-df", "--destinationFormat"}, () => settings.DestinationFormat ?? upload_format.xml, "The format to upload to the destination server"),
                new Option<bool>(new string[]{ "-t", "--testPackageOnly"}, () => settings.TestPackageOnly, "Only perform download and static analysis checks on the Package.\r\nDoes not require a DestinationServerAddress, will not try to connect to one if provided"),
                new Option<bool>(new string[] { "-vq", "--validateQuestionnaires" }, () => settings.ValidateQuestionnaires, "Include more extensive testing on Questionnaires (experimental)"),
                new Option<bool>(new string[]{ "-pdv", "--preventDuplicateCanonicalVersions"}, () => settings.PreventDuplicateCanonicalVersions, "Permit the tool to upload canonical resources even if they would result in the server having multiple canonical versions of the same resource after it runs\r\nThe requires the server to be able to handle resolving canonical URLs to the correct version of the resource desired by a particular call. Either via the versioned canonical reference, or using the logic defined in the $current-canonical operation"),
                new Option<bool>(new string[]{ "-cn", "--checkAndCleanNarratives"}, () => settings.CheckAndCleanNarratives, "Check and clean any narratives in the package and remove suspect ones\r\n(based on the MS FHIR Server's rules)"),
                new Option<bool>(new string[]{ "-c", "--checkPackageInstallationStateOnly"}, () => settings.CheckPackageInstallationStateOnly, "Download and check the package and compare with the contents of the FHIR Server,\r\n but do not update any of the contents of the FHIR Server"),
                new Option<bool>(new string[]{ "--includeExamples"}, () => settings.Verbose, "Also include files in the examples sub-directory\r\n(Still needs resource type specified)"),
                new Option<bool>(new string[]{ "--verbose"}, () => settings.Verbose, "Provide verbose diagnostic output while processing\r\n(e.g. Filenames processed)"),
                new Option<string>(new string[] { "-odf", "--outputDependenciesFile" }, () => settings.OutputDependenciesFile, "Write the list of dependencies discovered in the IG into a json file for post-processing"),
            };

            // Include the conditional validation rules to check that there is a source for the package to load from
            rootCommand.AddValidator((result) =>
            {
                List<string> conditionalRequiredParams = new List<string>();
                conditionalRequiredParams.AddRange(rootCommand.Options[0].Aliases);
                conditionalRequiredParams.AddRange(rootCommand.Options[1].Aliases);
                if (!args.Any(a => conditionalRequiredParams.Contains(a)))
                    result.ErrorMessage = "The sourcePackagePath and packageId are both missing, please provide one or the other to indicate where to load the package from";
            });

            rootCommand.Handler = CommandHandler.Create(async (Settings context) =>
            {
                try
                {
                    return await UploadPackage(context);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return -1;
                }
            });
            return await rootCommand.InvokeAsync(args);
        }

        public static async Task<int> UploadPackage(Settings settings)
        {
            OutputDependenciesFile dumpOutput = new OutputDependenciesFile();
            dumpOutput.name = settings.PackageId;
            dumpOutput.date = DateTime.Now.ToString("yyyyMMddHHmmss");

            // Prepare a temp working folder to hold this downloaded package
            Stream sourceStream;
            if (!string.IsNullOrEmpty(settings.SourcePackagePath) && !settings.SourcePackagePath.StartsWith("http"))
            {
                // This is a local path so we should just use that!
                // No need to check any of the package ID/Version stuff
                Console.WriteLine($"Using local package: {settings.SourcePackagePath}");
                byte[] packageRawContent = File.ReadAllBytes(settings.SourcePackagePath);
                sourceStream = new MemoryStream(packageRawContent);
                dumpOutput.url = settings.SourcePackagePath;
            }
            else
            {
                string tempFIGpath = Path.Combine(Path.GetTempPath(), "UploadFIG");
                string localPackagePath = Path.Combine(tempFIGpath, "demo-upload.tgz");
                if (!Directory.Exists(tempFIGpath))
                {
                    Directory.CreateDirectory(tempFIGpath);
                }

                byte[] examplesPkg;

                // Check with the registry (for all versions of the package)
                if (!string.IsNullOrEmpty(settings.PackageId))
                {
                    PackageClient pc = PackageClient.Create();
                    examplesPkg = await pc.GetPackage(new PackageReference(settings.PackageId, null));
                    string contents = Encoding.UTF8.GetString(examplesPkg);
                    var pl = JsonConvert.DeserializeObject<PackageListing>(contents);
                    Console.WriteLine($"Package ID: {pl?.Name}");
                    Console.WriteLine($"Available Versions: {String.Join(", ", pl.Versions.Keys)}");
                    if (!string.IsNullOrEmpty(settings.PackageVersion) && !pl.Versions.ContainsKey(settings.PackageVersion))
                    {
                        Console.Error.WriteLine($"Version {settings.PackageVersion} was not in the registered versions");
                        return -1;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(settings.PackageVersion))
                        {
                            settings.PackageVersion = pl.Versions.LastOrDefault().Key;
                            Console.WriteLine($"Selecting latest version of package {settings.PackageVersion}");
                        }
                        else
                        {
                            Console.WriteLine($"Using package version: {settings.PackageVersion}");
                        }
                    }
                    Console.WriteLine($"Package is for FHIR version: {pl.Versions[settings.PackageVersion].FhirVersion}");
                    Console.WriteLine($"Canonical URL: {pl.Versions[settings.PackageVersion].Url}");
                    Console.WriteLine($"{pl.Versions[settings.PackageVersion].Description}");
                    Console.WriteLine($"Direct location: {pl.Versions[settings.PackageVersion].Dist?.Tarball}");
                    localPackagePath = Path.Combine(tempFIGpath, $"{settings.PackageId}.tgz");
                    dumpOutput.url = pl.Versions[settings.PackageVersion].Dist?.Tarball;
                }

                // Download the file from the HL7 registry/or other location
                if (!System.IO.File.Exists(localPackagePath) || settings.ForceDownload)
                {
                    if (settings.Verbose)
                        Console.WriteLine($"Downloading to {localPackagePath}");

                    if (!string.IsNullOrEmpty(settings.SourcePackagePath))
                    {
                        Console.WriteLine($"Downloading from {settings.SourcePackagePath}");
                        // Direct download approach
                        using (HttpClient client = new HttpClient())
                        {
                            examplesPkg = await client.GetByteArrayAsync(settings.SourcePackagePath);
                        }
                        System.IO.File.WriteAllBytes(localPackagePath, examplesPkg);
                        dumpOutput.url = settings.SourcePackagePath;
                    }
                    else
                    {
                        PackageClient pc = PackageClient.Create();
                        Console.WriteLine($"Downloading {settings.PackageId}|{settings.PackageVersion} from {pc}");

                        // Firely Package Manager approach (this will download into the users profile .fhir folder)
                        var pr = new Firely.Fhir.Packages.PackageReference(settings.PackageId, settings.PackageVersion);
                        examplesPkg = await pc.GetPackage(pr);
                        System.IO.File.WriteAllBytes(localPackagePath, examplesPkg);
                    }
                    sourceStream = new MemoryStream(examplesPkg);
                }
                else
                {
                    // Local package was already downloaded
                    Console.WriteLine($"Reading (pre-downloaded) {localPackagePath}");
                    sourceStream = System.IO.File.OpenRead(localPackagePath);
                }
            }

            using (var md5 = MD5.Create())
            {
                Console.WriteLine($"MD5 Checksum: {BitConverter.ToString(md5.ComputeHash(sourceStream)).Replace("-", string.Empty)}");
                sourceStream.Seek(0, SeekOrigin.Begin);
            }

			// Validate the headers being applied
			if (settings.DestinationServerHeaders?.Any() == true)
			{
				Console.WriteLine("Headers:");
				foreach (var header in settings.DestinationServerHeaders)
				{
					if (header.Contains(":"))
					{
						var kv = header.Split(new char[] { ':' }, 2);
                        Console.WriteLine($"\t{kv[0].Trim()}: {kv[1].Trim()}");
                        if (kv[0].Trim().ToLower() == "authentication" && kv[1].Trim().ToLower().StartsWith("bearer"))
                            Console.WriteLine($"\t\tWARNING: '{kv[0].Trim()}' header was provided, should that be 'Authorization'?");
					}
				}
			}

			Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress);
            MemoryStream ms = new MemoryStream();
            using (gzipStream)
            {
                // Unzip the tar file into a memory stream
                await gzipStream.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);
            }
            var reader = new TarReader(ms);

            Common_Processor versionAgnosticProcessor = null;
            ExpressionValidator expressionValidator = null;
            FHIRVersion? fhirVersion = null;

            long successes = 0;
            long failures = 0;
            long validationErrors = 0;
            var sw = Stopwatch.StartNew();

            // Locate and read the package manifest to read the package dependencies
            PackageManifest manifest = null;
            TarEntry entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                // Read the package definition file
                if (entry.Name == "package/package.json")
                {
                    var stream = entry.DataStream;
                    using (stream)
                    {
                        try
                        {
                            StreamReader sr = new StreamReader(stream);
                            var content = sr.ReadToEnd();
                            manifest = PackageParser.ParseManifest(content);
                            if (manifest != null)
                            {
                                Console.WriteLine();

                                // Select the version of the processor to use
                                fhirVersion = VersionSelector.SelectVersion(manifest);
                                switch (fhirVersion)
                                {
                                    case FHIRVersion.N4_0:
                                        versionAgnosticProcessor = new R4_Processor();
                                        expressionValidator = new ExpressionValidatorR4(versionAgnosticProcessor, settings.ValidateQuestionnaires);
                                        break;
                                    case FHIRVersion.N4_3:
                                        versionAgnosticProcessor = new R4B_Processor();
                                        expressionValidator = new ExpressionValidatorR4B(versionAgnosticProcessor, settings.ValidateQuestionnaires);
                                        break;
                                    case FHIRVersion.N5_0:
                                        versionAgnosticProcessor = new R5_Processor();
                                        expressionValidator = new ExpressionValidatorR5(versionAgnosticProcessor, settings.ValidateQuestionnaires);
                                        break;
                                    default:
                                        Console.Error.WriteLine($"Unsupported FHIR version: {manifest.GetFhirVersion()} from {string.Join(',', manifest.FhirVersions)}");
                                        return -1;
                                }
                                if (manifest.FhirVersions?.Count > 1 || manifest.FhirVersionList?.Count > 1)
                                    Console.WriteLine($"Detected FHIR Version {fhirVersion} from {string.Join(',', manifest.FhirVersions)}");
                                else
                                    Console.WriteLine($"Detected FHIR Version {fhirVersion}");
                                Console.WriteLine();

                                Console.WriteLine("Package dependencies:");
                                if (manifest.Dependencies != null)
                                    Console.WriteLine($"    {string.Join("\r\n    ", manifest.Dependencies.Select(d => $"{d.Key}|{d.Value}"))}");
                                else
                                    Console.WriteLine($"    (none)");
                                Console.WriteLine();
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading package.json: {ex.Message}");
                            return -1;
                        }
                    }
                }
            }
            // skip back to the start (for cases where the pacakge.json isn't the first resource)
            ms.Seek(0, SeekOrigin.Begin);
            reader = new TarReader(ms);

            // Stash output data
            dumpOutput.title = manifest.Title;
            dumpOutput.fhirVersion = fhirVersion.GetLiteral();
            dumpOutput.version = manifest.Version.ToString();
            foreach (var item in manifest.Dependencies)
            {
                dumpOutput.dependencies.Add(item.Key, item.Value);
            }

			var errs = new List<String>();
            var errFiles = new List<String>();
            
            // Server to upload the resources to
            BaseFhirClient clientFhir = null;
            if (!string.IsNullOrEmpty(settings.DestinationServerAddress))
            {
                // Need to pass through the destination header too
                HttpClient client = new HttpClient();
                if (settings.DestinationServerHeaders?.Any() == true)
                {
                    foreach (var header in settings.DestinationServerHeaders)
                    {
                        if (header.Contains(":"))
                        {
                            var kv = header.Split(new char[] { ':' }, 2);
                            client.DefaultRequestHeaders.Add(kv[0].Trim(), kv[1].Trim());
                        }
                    }
                }
                clientFhir = new BaseFhirClient(new Uri(settings.DestinationServerAddress), client, versionAgnosticProcessor.ModelInspector);
                if (settings.DestinationFormat == upload_format.json)
                    clientFhir.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json;
                if (settings.DestinationFormat == upload_format.xml)
                    clientFhir.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Xml;
                clientFhir.Settings.VerifyFhirVersion = true;
            }

            // Load all the content in so that it can then be re-sequenced
            Console.WriteLine("--------------------------------------");
            Console.WriteLine("");
            Console.WriteLine("Scanning package content:");
            List<Resource> resourcesToProcess = new();
			while ((entry = reader.GetNextEntry()) != null)
			{
				if (SkipFile(settings, entry.Name))
                    continue;
                if (entry.EntryType != TarEntryType.Directory)
                {
                    var exampleName = entry.Name;
                    if (settings.Verbose)
                        Console.WriteLine($"Processing: {exampleName}");
                    var stream = entry.DataStream;
                    using (stream)
                    {
                        Resource resource = null;
                        try
                        {
                            if (exampleName.EndsWith(".xml"))
                            {
                                using (var xr = SerializationUtil.XmlReaderFromStream(stream))
                                {
                                    resource = versionAgnosticProcessor.Parse(xr);
                                }
                            }
                            else if (exampleName.EndsWith(".json"))
                            {
                                using (var jr = SerializationUtil.JsonReaderFromStream(stream))
                                {
                                    resource = versionAgnosticProcessor.Parse(jr);
                                }
                            }
                            else
                            {
                                // Not a file that we can process
                                // (What about fml/map files?)
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"ERROR: ({exampleName}) {ex.Message}");
                            System.Threading.Interlocked.Increment(ref failures);
                            if (!errs.Contains(ex.Message))
                                errs.Add(ex.Message);
                            errFiles.Add(exampleName);
                            continue;
                        }

                        // Skip resource types we're not intentionally importing
                        // (usually examples)
                        if (!settings.ResourceTypes.Contains(resource.TypeName))
                        {
                            if (settings.Verbose)
                                Console.WriteLine($"    ----> Ignoring {exampleName} because {resource.TypeName} is not a requested type");
                            continue;
                        }

                        resourcesToProcess.Add(resource);
                        resource.SetAnnotation(new ExampleName() { value = exampleName });
                    }
                }
            }

            // We grab a list of ALL the search parameters we come across to process them at the end - as composites need cross validation
            expressionValidator.PreValidation(resourcesToProcess);

            foreach (var resource in resourcesToProcess)
            {
                var exampleName = resource.Annotation<ExampleName>().value;
                try
                {
                    // Workaround for loading packages with invalid xhmtl content - strip them
                    if (resource is DomainResource dr && resource is IVersionableConformanceResource ivr)
                    {
                        if (settings.IgnoreCanonicals?.Contains(ivr.Url) == true)
                        {
                            if (settings.Verbose)
                                Console.WriteLine($"    ----> Ignoring {exampleName} because it is in the ignore list canonical: {ivr.Url}");
                            continue;
                        }
                        // lets validate this xhtml content before trying
                        if (settings.CheckAndCleanNarratives && !string.IsNullOrEmpty(dr.Text?.Div))
                        {
                            if (settings.Verbose)
                                Console.WriteLine($"    ----> Checking narrative text in canonical: {ivr.Url}");

                            var messages = NarrativeHtmlSanitizer.Validate(dr.Text.Div);
                            if (messages.Any())
                            {
                                Console.WriteLine($"    ----> stripped potentially corrupt narrative from {exampleName}");
                                //Console.WriteLine(dr.Text?.Div);
                                //Console.WriteLine("----");

                                // strip out the narrative as we don't really need that for the purpose
                                // of validations.
                                dr.Text = null;
                            }
                        }
                    }

                    if (!expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles))
                        continue;

                    if (!settings.TestPackageOnly && !string.IsNullOrEmpty(settings.DestinationServerAddress))
                    {
                        Resource result = UploadFile(settings, clientFhir, resource);
                        if (result != null || settings.CheckPackageInstallationStateOnly)
                            System.Threading.Interlocked.Increment(ref successes);
                        else
                            System.Threading.Interlocked.Increment(ref failures);
                    }
                    else
                    {
                        System.Threading.Interlocked.Increment(ref successes);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"ERROR: ({exampleName}) {ex.Message}");
                    System.Threading.Interlocked.Increment(ref failures);
                    // DebugDumpOutputXml(resource);
                    errFiles.Add(exampleName);
                }
            }

            // Scan through the resources and resolve any canonicals
            Console.WriteLine();
            Console.WriteLine("--------------------------------------");
            List<CanonicalDetails> requiresCanonicals = DependencyChecker.ScanForCanonicals(fhirVersion.Value, resourcesToProcess, versionAgnosticProcessor);
            DependencyChecker.VerifyDependenciesOnServer(settings, clientFhir, requiresCanonicals);

            sw.Stop();
            Console.WriteLine("Done!");
                Console.WriteLine();

            if (errs.Any() || errFiles.Any())
            {
                Console.WriteLine("--------------------------------------");
                Console.WriteLine(String.Join("\r\n", errs));
                Console.WriteLine("--------------------------------------");
                Console.WriteLine(String.Join("\r\n", errFiles));
                Console.WriteLine("--------------------------------------");
                Console.WriteLine();
            }
            if (settings.TestPackageOnly)
            {
                // A canonical resource review table
                Console.WriteLine("Package Canonical content summary:");
                Console.WriteLine("\tCanonical Url\tCanonical Version\tStatus\tName");
                foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
                {
                    Console.WriteLine($"\t{resource.Url}\t{resource.Version}\t{resource.Status}\t{resource.Name}");
                }
                Console.WriteLine("--------------------------------------");

                // Dependant Canonical Resources
                Console.WriteLine("Requires the following non-core canonical resources:");
                Console.WriteLine("\tResource Type\tCanonical Url\tVersion");
                foreach (var details in requiresCanonicals)
                {
                    Console.WriteLine($"\t{details.resourceType}\t{details.canonical}\t{details.version}");
                }
                Console.WriteLine("--------------------------------------");

                Console.WriteLine("Package Resource type summary:");
                Console.WriteLine("\tType\tCount");
                foreach (var resource in resourcesToProcess.GroupBy(f => f.TypeName).OrderBy(f => f.Key))
                {
					Console.WriteLine($"\t{resource.Key}\t{resource.Count()}");
				}
				Console.WriteLine($"\tTotal\t{resourcesToProcess.Count()}");
                Console.WriteLine("--------------------------------------");

				// And the summary at the end
				Console.WriteLine("");
                Console.WriteLine($"Checked: {successes}");
                Console.WriteLine($"Validation Errors: {validationErrors}");
            }
            else
            {
				Console.WriteLine("Package Resource type summary:");
				Console.WriteLine("\tType\tCount");
				foreach (var resource in resourcesToProcess.GroupBy(f => f.TypeName).OrderBy(f => f.Key))
				{
					Console.WriteLine($"\t{resource.Key}\t{resource.Count()}");
				}
				Console.WriteLine($"\tTotal\t{resourcesToProcess.Count()}");
                Console.WriteLine("--------------------------------------");

				// And the summary at the end
				Console.WriteLine($"Success: {successes}");
                Console.WriteLine($"Failures: {failures}");
                Console.WriteLine($"Validation Errors: {validationErrors}");
                Console.WriteLine($"Duration: {sw.Elapsed.ToString()}");
                Console.WriteLine($"rps: {(successes + failures) / sw.Elapsed.TotalSeconds}");
            }

            if (!string.IsNullOrEmpty(settings.OutputDependenciesFile))
            {
                foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
                {
                    dumpOutput.containedCanonicals.Add(new CanonicalDetails()
                    {
                        resourceType = (resource as Resource).TypeName,
                        canonical = resource.Url,
                        version = resource.Version,
                        status = resource.Status.GetLiteral(),
                        name = resource.Name,
                    });
                    // Console.WriteLine($"\t{resource.Url}\t{resource.Version}\t{resource.Status}\t{resource.Name}");
                }
                dumpOutput.externalCanonicalsRequired.AddRange(
                    requiresCanonicals.Select(rc => new DependentResource()
                    {
                        resourceType = rc.resourceType,
                        canonical = rc.canonical,
                        version = rc.version,
                    })
                    );
                try
                {
                    // Write dumpOutput to a JSON string
                    JsonSerializerSettings serializerSettings = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };
                    System.IO.File.WriteAllText(settings.OutputDependenciesFile, JsonConvert.SerializeObject(dumpOutput, serializerSettings));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing dependencies summary to {settings.OutputDependenciesFile}: {ex.Message}");
                }
            }
            return 0;
        }

        record ExampleName
        {
            public string value { get; init; }
        }

        static bool SkipFile(Settings settings, string filename)
        {
            if (settings.SelectFiles?.Any() == true)
            {
                if (settings.SelectFiles.Contains(filename))
                    return false;
                return true;
            }
            if (!settings.IncludeExamples && filename.StartsWith("package/example/"))
            {
                if (settings.Verbose)
                    Console.WriteLine($"Ignoring:   {filename}    (example)");
                return true;
            }
            if (settings.IgnoreFiles?.Contains(filename) == true)
            {
                if (settings.Verbose)
                    Console.WriteLine($"Ignoring:   {filename}    because it is in the ignore list");
                return true;
            }
            // Schematron files typically included in the package are not wanted
            if (filename.EndsWith(".sch"))
                return true;

            // The package index file isn't to be uploaded
            if (filename.EndsWith("/package.json"))
                return true;
            if (filename.EndsWith(".index.json"))
                return true;
            if (filename.EndsWith(".openapi.json"))
                return true;
            if (filename.EndsWith(".schema.json"))
                return true;

            // Other internal Package files aren't to be considered either
            if (filename.EndsWith("spec.internals"))
                return true;
            if (filename.EndsWith("validation-summary.json"))
                return true;
            if (filename.EndsWith("validation-oo.json"))
                return true;

            return false;
        }

        static Resource? UploadFile(Settings settings, BaseFhirClient clientFhir, Resource resource)
        {
            // Check to see if the resource is the same on the server already
            // (except for text/version/modified)
            var oldColor = Console.ForegroundColor;
            try
            {
                // reset properties that are set on the server anyway
                if (resource.Meta == null) resource.Meta = new Meta();
                resource.Meta.LastUpdated = null;
                resource.Meta.VersionId = null;

                Resource? current = null;
                if (!string.IsNullOrEmpty(resource.Id))
                    current = clientFhir.Get($"{resource.TypeName}/{resource.Id}");
                if (current != null)
                {
                    Resource original = (Resource)current.DeepCopy();
                    current.Meta.LastUpdated = null;
                    current.Meta.VersionId = null;
                    if (current is DomainResource dr)
                        dr.Text = (resource as DomainResource)?.Text;
                    if (current.IsExactly(resource))
                    {
                        Console.WriteLine($"    {original.TypeName}/{original.Id} unchanged {(resource as IVersionableConformanceResource)?.Version}");
                        return original;
                    }
                }
            }
            catch (FhirOperationException fex)
            {
                if (fex.Status != System.Net.HttpStatusCode.NotFound && fex.Status != System.Net.HttpStatusCode.Gone)
                {
                    Console.Error.WriteLine($"Warning: {resource.TypeName}/{resource.Id} {fex.Message}");
                }
            }


            string warningMessage = null;
            if (resource is IVersionableConformanceResource vcs)
            {
                try
                {
                    // Search to locate any existing versions of this canonical resource
                    var others = clientFhir.Search(resource.TypeName, new[] { $"url={vcs.Url}" });
                    var existingResources = others.Entry.Where(e => e.Resource?.TypeName == resource.TypeName).Select(e => e.Resource).ToList();
                    if (existingResources.Count(e => (e as IVersionableConformanceResource).Version == vcs.Version) > 1)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"ERROR: Canonical {vcs.Url}|{vcs.Version} has multiple instances already loaded - Must be resolved manually as unable to select which to update");
                        Console.ForegroundColor = oldColor;
                        return null;
                    }
                    var existingVersion = existingResources.FirstOrDefault(e => (e as IVersionableConformanceResource).Version == vcs.Version);
                    var otherCanonicalVersionNumbers = existingResources.Select(e => (e as IVersionableConformanceResource)?.Version).Where(v => v != vcs.Version).ToList();

                    // Select the existing resource to "refresh" the entry to what was in the implementation guide,
                    // or clear the ID to let the server allocate the resource ID
                    resource.Id = existingVersion?.Id;

                    if (otherCanonicalVersionNumbers.Any())
                    {
                        if (settings.PreventDuplicateCanonicalVersions && resource.Id == null)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} already has other versions loaded - {string.Join(", ", otherCanonicalVersionNumbers)}, can't also load {vcs.Version}, adding may cause issues if the server can't determine which is the latest to use");
                            Console.ForegroundColor = oldColor;
                            return null;
                        }
                        warningMessage = $"Warning: other versions already loaded ({string.Join(", ", otherCanonicalVersionNumbers)})";
                    }

                    if (resource.Id != null)
                    {
                        // This is an update of the canonical version, check to see if there is a change or that we can just skip loading
                        Resource original = (Resource)existingVersion.DeepCopy();
                        existingVersion.Meta.LastUpdated = null;
                        existingVersion.Meta.VersionId = null;
                        if (existingVersion is DomainResource dr)
                            dr.Text = (resource as DomainResource)?.Text;
                        if (existingVersion.IsExactly(resource))
                        {
                            Console.Write($"    unchanged\t{existingVersion.TypeName}\t{(resource as IVersionableConformanceResource)?.Url}|{(resource as IVersionableConformanceResource)?.Version}");
                            if (!string.IsNullOrEmpty(warningMessage))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.Write($"\t{warningMessage}");
                            }
                            Console.ForegroundColor = oldColor;
                            Console.WriteLine();
                            return original;
                        }
                    }
                }
                catch (FhirOperationException fex)
                {
                    if (fex.Status != System.Net.HttpStatusCode.NotFound && fex.Status != System.Net.HttpStatusCode.Gone)
                    {
                        Console.Error.WriteLine($"Warning: {resource.TypeName}/{resource.Id} {fex.Message} {vcs.Url}|{vcs.Version}");
                    }
                }
            }

            // Now that we've established that it is new/different, upload it
            if (settings.CheckPackageInstallationStateOnly)
                return null;

            Resource result;
            string operation = string.IsNullOrEmpty(resource.Id) ? "created" : "updated";
            if (!string.IsNullOrEmpty(resource.Id))
                result = clientFhir.Update(resource);
            else
                result = clientFhir.Create(resource);

            Console.ForegroundColor = ConsoleColor.DarkGreen;
            if (result is IVersionableConformanceResource r)
                Console.Write($"    {operation}\t{result.TypeName}\t{r.Url}|{r.Version}");
            else
                Console.Write($"    {operation}\t{result.TypeName}/{result.Id} {result.VersionId}");
            if (!string.IsNullOrEmpty(warningMessage))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"\t{warningMessage}");
            }
            Console.ForegroundColor = oldColor;
            Console.WriteLine();
            return result;
        }
    }
}