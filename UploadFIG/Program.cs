// See https://aka.ms/new-console-template for more information
// using AngleSharp;
using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SharpCompress.Readers;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
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
                new Option<List<string>>(new string[]{ "-if", "--ignoreFiles" }, () => settings.IgnoreFiles, "Any specific files that should be ignored/skipped when processing the package"),
                new Option<List<string>>(new string[]{ "-ic", "--ignoreCanonicals" }, () => settings.IgnoreCanonicals, "Any specific Canonical URls that should be ignored/skipped when processing the package"),
                new Option<string>(new string[]{ "-d", "--destinationServerAddress" }, () => settings.DestinationServerAddress, "The URL of the FHIR Server to upload the package contents to"),
                new Option<List<string>>(new string[]{ "-dh", "--destinationServerHeaders"}, () => settings.DestinationServerHeaders, "Headers to add to the request to the destination FHIR Server"),
                new Option<upload_format>(new string[]{ "-df", "--destinationFormat"}, () => settings.DestinationFormat ?? upload_format.xml, "The format to upload to the destination server"),
                new Option<bool>(new string[]{ "-t", "--testPackageOnly"}, () => settings.TestPackageOnly, "Only perform download and static analysis checks on the Package.\r\nDoes not require a DestinationServerAddress, will not try to connect to one if provided"),
                new Option<bool>(new string[]{ "-pdv", "--preventDuplicateCanonicalVersions"}, () => settings.PreventDuplicateCanonicalVersions, "Permit the tool to upload canonical resources even if they would result in the server having multiple canonical versions of the same resource after it runs\r\nThe requires the server to be able to handle resolving canonical URLs to the correct version of the resource desired by a particular call. Either via the versioned canonical reference, or using the logic defined in the $current-canonical operation"),
                new Option<bool>(new string[]{ "-cn", "--checkAndCleanNarratives"}, () => settings.CheckAndCleanNarratives, "Check and clean any narratives in the package and remove suspect ones\r\n(based on the MS FHIR Server's rules)"),
                new Option<bool>(new string[]{ "-c", "--checkPackageInstallationStateOnly"}, () => settings.CheckPackageInstallationStateOnly, "Download and check the package and compare with the contents of the FHIR Server,\r\n but do not update any of the contents of the FHIR Server"),
                new Option<bool>(new string[]{ "--includeExamples"}, () => settings.Verbose, "Also include files in the examples sub-directory\r\n(Still needs resource type specified)"),
                new Option<bool>(new string[]{ "--verbose"}, () => settings.Verbose, "Provide verbose diagnostic output while processing\r\n(e.g. Filenames processed)"),
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
            // Prepare a temp working folder to hold this downloaded package
            Stream sourceStream;
            if (!string.IsNullOrEmpty(settings.SourcePackagePath) && !settings.SourcePackagePath.StartsWith("http"))
            {
                // This is a local path so we should just use that!
                // No need to check any of the package ID/Version stuff
                Console.WriteLine($"Using local package: {settings.SourcePackagePath}");
                byte[] packageRawContent = File.ReadAllBytes(settings.SourcePackagePath);
                sourceStream = new MemoryStream(packageRawContent);
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

            Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress);
            MemoryStream ms = new MemoryStream();
            using (gzipStream)
            {
                // Unzip the tar file into a memory stream
                await gzipStream.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);
            }
            var reader = ReaderFactory.Open(ms);

            long successes = 0;
            long failures = 0;
            long validationErrors = 0;
            var sw = Stopwatch.StartNew();

            ExpressionValidator expressionValidator = new ExpressionValidator();

            // disable validation during parsing (not its job)
            var jsparser = new FhirJsonParser(new ParserSettings() { AcceptUnknownMembers = true, AllowUnrecognizedEnums = true, PermissiveParsing = true });
            var xmlParser = new FhirXmlParser();

            var errs = new List<String>();
            var errFiles = new List<String>();

            // Server to upload the resources to
            FhirClient clientFhir = null;
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
                            client.DefaultRequestHeaders.Add(kv[0], kv[1]);
                        }
                    }
                }
                clientFhir = new FhirClient(settings.DestinationServerAddress, client);
                if (settings.DestinationFormat == upload_format.json)
                    clientFhir.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json;
                if (settings.DestinationFormat == upload_format.xml)
                    clientFhir.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Xml;
            }

            PackageManifest manifest = null;
            while (reader.MoveToNextEntry())
            {
                // Read the package definition file
                if (reader.Entry.Key == "package/package.json")
                {
                    var stream = reader.OpenEntryStream();
                    using (stream)
                    {
                        try
                        {
                            StreamReader sr = new StreamReader(stream);
                            var content = sr.ReadToEnd();
                            manifest = PackageParser.ParseManifest(content);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading package.json: {ex.Message}");
                            return -1;
                        }
                    }
                }
                if (SkipFile(settings, reader.Entry.Key))
                    continue;
                if (!reader.Entry.IsDirectory)
                {
                    var exampleName = reader.Entry.Key;
                    if (settings.Verbose)
                        Console.WriteLine($"Processing: {exampleName}");
                    var stream = reader.OpenEntryStream();
                    using (stream)
                    {
                        Resource resource;
                        try
                        {
                            if (exampleName.EndsWith(".xml"))
                            {
                                using (var xr = SerializationUtil.XmlReaderFromStream(stream))
                                {
                                    resource = xmlParser.Parse<Resource>(xr);
                                }
                            }
                            else
                            {
                                using (var jr = SerializationUtil.JsonReaderFromStream(stream))
                                {
                                    resource = jsparser.Parse<Resource>(jr);
                                }
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

                            if (resource is SearchParameter sp)
                            {
                                if (!sp.Base.Any())
                                {
                                    // Quietly skip them
                                    Console.Error.WriteLine($"ERROR: ({exampleName}) Search parameter with no base");
                                    System.Threading.Interlocked.Increment(ref failures);
                                    // DebugDumpOutputXml(resource);
                                    errFiles.Add(exampleName);
                                    continue;
                                }
                                if (!expressionValidator.ValidateSearchExpression(sp))
                                    validationErrors++;

                            }

                            if (resource is StructureDefinition sd)
                            {
                                if (!expressionValidator.ValidateInvariants(sd))
                                    validationErrors++;
                            }

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
                }
            }

            if (manifest != null)
            {
                Console.WriteLine("");
                Console.WriteLine("Package dependencies:");
                Console.WriteLine($"    {string.Join("\r\n    ", manifest.Dependencies.Select(d => $"{d.Key}|{d.Value}"))}");
            }

            sw.Stop();
            Console.WriteLine("Done!");
            Console.WriteLine();

            if (errs.Any() || errFiles.Any())
            {
                Console.WriteLine("-----------------------------------");
                Console.WriteLine(String.Join("\r\n", errs));
                Console.WriteLine("-----------------------------------");
                Console.WriteLine(String.Join("\r\n", errFiles));
                Console.WriteLine("-----------------------------------");
            }
            if (settings.TestPackageOnly)
            {
                Console.WriteLine($"Checked: {successes}");
                Console.WriteLine($"Validation Errors: {validationErrors}");
            }
            else
            {
                Console.WriteLine($"Success: {successes}");
                Console.WriteLine($"Failures: {failures}");
                Console.WriteLine($"Validation Errors: {validationErrors}");
                Console.WriteLine($"Duration: {sw.Elapsed.ToString()}");
                Console.WriteLine($"rps: {(successes + failures) / sw.Elapsed.TotalSeconds}");
            }

            return 0;
        }

        static bool SkipFile(Settings settings, string filename)
        {
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

            // Other internal Package files aren't to be considered either
            if (filename.EndsWith("spec.internals"))
                return true;
            if (filename.EndsWith("validation-summary.json"))
                return true;
            if (filename.EndsWith("validation-oo.json"))
                return true;

            return false;
        }

        static Resource? UploadFile(Settings settings, FhirClient clientFhir, Resource resource)
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
                        Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} unchanged");
                        return original;
                    }
                }


                // Update narratives to strip relatives?
                // https://chat.fhir.org/#narrow/stream/179166-implementers/topic/Narrative.20image.20sources
                // ensure has the generated tag before just deleting

                if (resource is IVersionableConformanceResource vcs)
                {
                    // Also search to see if there is another canonical version of this instance that would clash with it
                    var others = clientFhir.Search(resource.TypeName, new[] { $"url={vcs.Url}" });
                    if (others.Entry.Count > 1)
                    {
                        var versionList = others.Entry.Select(e => (e.Resource as IVersionableConformanceResource)?.Version).ToList();
                        if (settings.PreventDuplicateCanonicalVersions)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                            Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} already has multiple copies loaded - {string.Join(", ", versionList)}");
                            Console.ForegroundColor = oldColor;
                            return null;
                        }
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} warning");
                        Console.Error.WriteLine($"Warning: Canonical {vcs.Url}|{vcs.Version} has other versions {string.Join(", ", versionList)} already loaded");
                        Console.ForegroundColor = oldColor;
                    }
                    // And check that the one we're loading in has the same ID
                    if (others.Entry.Count == 1)
                    {
                        var currentFound = others.Entry[0].Resource as IVersionableConformanceResource;
                        // Don't know how this could ever be tripped on, the search is on the resource type
                        if (others.Entry[0].Resource?.TypeName != resource.TypeName)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                            Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} returned a different type");
                            Console.ForegroundColor = oldColor;
                            return null;
                        }
                        if (currentFound.Version != vcs.Version)
                        {
                            if (settings.PreventDuplicateCanonicalVersions)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                                Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} has version {currentFound.Version} already loaded, can't also load {vcs.Version}");
                                Console.ForegroundColor = oldColor;
                                return null;
                            }
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} warning");
                            Console.Error.WriteLine($"Warning: Canonical {vcs.Url}|{vcs.Version} has another version {currentFound.Version} already loaded, adding may cause issues if the server can't determine which is the latest to use");
                            Console.ForegroundColor = oldColor;
                        }
                        if (string.IsNullOrEmpty(resource.Id))
                        {
                            // Use the same resource ID
                            // (as was expecting to use the server assigned ID - don't expect to hit here as standard packaged resources have an ID from the IG publisher)
                            resource.Id = others.Entry[0].Resource.Id;
                        }
                        else if (others.Entry[0].Resource.Id != resource.Id)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} error");
                            Console.Error.WriteLine($"ERROR: Canonical {vcs.Url} has id {others.Entry[0].Resource.Id} on the server, can't also load id {resource.Id}");
                            Console.ForegroundColor = oldColor;
                            return null;
                        }
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


            // Now that we've established that it is new/different, upload it
            if (settings.CheckPackageInstallationStateOnly)
                return null;

            Resource result;
            if (!string.IsNullOrEmpty(resource.Id))
                result = clientFhir.Update(resource);
            else
                result = clientFhir.Create(resource);

            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"    {resource.TypeName}/{resource.Id} {resource.VersionId} uploaded");
            Console.ForegroundColor = oldColor;
            return result;
        }
    }
}