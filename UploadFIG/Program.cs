extern alias r4;
extern alias r4b;
extern alias r5;

using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Snapshot;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Specialized;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using UploadFIG.Helpers;
using UploadFIG.PackageHelpers;

namespace UploadFIG
{
    public class Program
    {
        public static HttpClient useClient;
        public static long successes = 0;
        public static long failures = 0;
        public static long validationErrors = 0;

        /// <summary>Main entry-point for this application.</summary>
        /// <param name="args">An array of command-line argument strings.</param>
        public static async Task<int> Main(string[] args)
        {
            successes = 0;
            failures = 0;
            validationErrors = 0;

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
            ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");

            // source parameters
            var sourceOption = new Option<string>(new string[] { "-s", "--sourcePackagePath" }, () => settings.SourcePackagePath, "The explicit path of a package to process (over-rides PackageId/Version)");
            var packageIdOption = new Option<string>(new string[] { "-pid", "--packageId" }, () => settings.PackageId, "The Package ID of the package to upload (from the HL7 FHIR Package Registry)");

            // target/test mode parameters
            var destinationServerOption = new Option<string>(new string[] { "-d", "--destinationServerAddress" }, () => settings.DestinationServerAddress, "The URL of the FHIR Server to upload the package contents to");
            var testPackageOnlyOption = new Option<bool>(new string[] { "-t", "--testPackageOnly" }, () => settings.TestPackageOnly, "Only perform download and static analysis checks on the Package.\r\nDoes not require a DestinationServerAddress, will not try to connect to one if provided");

            var rootCommand = new RootCommand("HL7 FHIR Implementation Guide Uploader")
            {
                // Mandatory parameters
                sourceOption,
                packageIdOption,

                // Optional parameters
                new Option<bool>(new string[]{ "-fd", "--forceDownload"}, () => settings.ForceDownload, "Force the download of the package from the source package path\r\n(If not specified, will use the last downloaded package)"),
                new Option<string>(new string[]{ "-pv", "--packageVersion"}, () => settings.PackageVersion, "The version of the Package to upload (from the HL7 FHIR Package Registry)"),
                new Option<List<string>>(new string[]{ "-r", "--resourceTypes"}, () => settings.ResourceTypes, "Which resource types should be processed by the uploader"),
                new Option<List<string>>(new string[]{ "-sf", "--selectFiles"}, () => settings.SelectFiles, "Only process these selected files\r\n(e.g. package/SearchParameter-valueset-extensions-ValueSet-end.json)"),
                new Option<List<string>>(new string[]{ "-ap", "--additionalPackages"}, () => settings.AdditionalPackages, "Set of additional packages to include in the processing\r\nThese will be processes as though they are dependencies of the root package"),
                new Option<List<string>>(new string[]{ "-if", "--ignoreFiles" }, () => settings.IgnoreFiles, "Any specific files that should be ignored/skipped when processing the package"),
                new Option<List<string>>(new string[]{ "-ic", "--ignoreCanonicals" }, () => settings.IgnoreCanonicals, "Any specific Canonical URls that should be ignored/skipped when processing the package and resource dependencies"),
                new Option<List<string>>(new string[]{ "-ip", "--ignorePackages" }, () => settings.IgnorePackages, "While loading in dependencies, ignore these versioned packages. e.g. us.nlm.vsac|0.18.0" ),
                destinationServerOption,
                new Option<List<string>>(new string[]{ "-dh", "--destinationServerHeaders"}, () => settings.DestinationServerHeaders, "Headers to add to the request to the destination FHIR Server"),
                new Option<upload_format>(new string[]{ "-df", "--destinationFormat"}, () => settings.DestinationFormat ?? upload_format.xml, "The format to upload to the destination server"),
                testPackageOnlyOption,
                new Option<bool>(new string[] { "-vq", "--validateQuestionnaires" }, () => settings.ValidateQuestionnaires, "Include more extensive testing on Questionnaires (experimental)"),
                new Option<bool>(new string[] { "-vrd", "--validateReferencedDependencies" }, () => settings.ValidateReferencedDependencies, "Validate any referenced resources from dependencies being installed"),
                new Option<bool>(new string[]{ "-pdv", "--preventDuplicateCanonicalVersions"}, () => settings.PreventDuplicateCanonicalVersions, "Permit the tool to upload canonical resources even if they would result in the server having multiple canonical versions of the same resource after it runs\r\nThe requires the server to be able to handle resolving canonical URLs to the correct version of the resource desired by a particular call. Either via the versioned canonical reference, or using the logic defined in the $current-canonical operation"),
                new Option<bool>(new string[]{ "-cn", "--checkAndCleanNarratives"}, () => settings.CheckAndCleanNarratives, "Check and clean any narratives in the package and remove suspect ones\r\n(based on the MS FHIR Server's rules)"),
				new Option<bool>(new string[]{ "-sn", "--stripNarratives"}, () => settings.StripNarratives, "Strip all narratives from the resources in the package"),
				new Option<bool>(new string[]{ "-c", "--checkPackageInstallationStateOnly"}, () => settings.CheckPackageInstallationStateOnly, "Download and check the package and compare with the contents of the FHIR Server,\r\n but do not update any of the contents of the FHIR Server"),
                new Option<bool>(new string[]{ "-gs", "--generateSnapshots"}, () => settings.GenerateSnapshots, "Generate the snapshots for any missing snapshots in StructureDefinitions"),
                new Option<bool>(new string[]{ "-rs", "--regenerateSnapshots"}, () => settings.ReGenerateSnapshots, "Re-Generate all snapshots in StructureDefinitions"),
				new Option<bool>(new string[]{ "-rms", "--removeSnapshots"}, () => settings.RemoveSnapshots, "Remove all snapshots in StructureDefinitions"),
				new Option<bool>(new string[]{ "-pcv", "--patchCanonicalVersions"}, () => settings.PatchCanonicalVersions, "Patch canonical URL references to be version specific where they resolve within the package"),
				new Option<bool>(new string[] { "--includeReferencedDependencies" }, () => settings.IncludeReferencedDependencies, "Upload any referenced resources from resource dependencies being included"),
                new Option<bool>(new string[]{ "--includeExamples"}, () => settings.IncludeExamples, "Also include files in the examples sub-directory\r\n(Still needs resource type specified)"),
                new Option<bool>(new string[]{ "--verbose"}, () => settings.Verbose, "Provide verbose diagnostic output while processing\r\n(e.g. Filenames processed)"),
                new Option<string>(new string[] { "-of", "--outputBundle" }, () => settings.OutputBundle, "The filename to write a json batch bundle containing all of the processed resources into (could be used in place of directly deploying the IG)"),
                new Option<string>(new string[] { "-odf", "--outputDependenciesFile" }, () => settings.OutputDependenciesFile, "Write the list of dependencies discovered in the IG into a json file for post-processing"),
                new Option<string>(new string[] { "-reg", "--externalRegistry" }, () => settings.ExternalRegistry, "The URL of an external FHIR server to use for resolving resources not already on the destination server"),
                new Option<List<string>>(new string[] { "-regh", "--externalRegistryHeaders" }, () => settings.ExternalRegistryHeaders, "Additional headers to supply when connecting to the external FHIR server"),
                new Option<string>(new string[] { "-rego", "--externalRegistryExportFile" }, () => settings.ExternalRegistryExportFile, "The filename of a file to write the json bundle of downloaded registry resources to"),
                new Option<string>(new string[] { "-ets", "--externalTerminologyServer" }, () => settings.ExternalTerminologyServer, "The URL of an external FHIR terminology server to use for creating expansions (where not on an external registry)"),
                new Option<List<string>>(new string[] { "-etsh", "--externalTerminologyServerHeaders" }, () => settings.ExternalTerminologyServerHeaders, "Additional headers to supply when connecting to the external FHIR terminology server"),
                new Option<long?>(new string [] { "-mes", "--maxExpansionSize" }, () => settings.MaxExpansionSize, "The maximum number of codes to include in a ValueSet expansion"),
            };

            // Include the conditional validation rules to check that there is a source for the package to load from
            // and also check that there is a destination or test mode flag provided
            rootCommand.AddValidator((result) =>
            {
                List<string> conditionalRequiredParams = new List<string>();
                conditionalRequiredParams.AddRange(sourceOption.Aliases);
                conditionalRequiredParams.AddRange(packageIdOption.Aliases);
                if (!args.Any(a => conditionalRequiredParams.Contains(a)))
                    result.ErrorMessage = "The sourcePackagePath and packageId are both missing, please provide one or the other to indicate where to load the package from";

                List<string> conditionalRequiredParams2 = new List<string>();
                conditionalRequiredParams2.AddRange(destinationServerOption.Aliases);
                conditionalRequiredParams2.AddRange(testPackageOnlyOption.Aliases);
                if (!args.Any(a => conditionalRequiredParams2.Contains(a)))
                    result.ErrorMessage = "The destinationServerAddress and testPackageOnly are both missing, please provide one or the other to indicate if just testing, or uploading to a server";
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
			Bundle alternativeOutputBundle = new Bundle() { Type = Bundle.BundleType.Batch };

			// Validate specific headers being applied for common errors
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

			// Prepare a temp working folder to hold this downloaded package
			Stream sourceStream = await GetSourcePackageStream(settings, dumpOutput);
			if (sourceStream == null)
				return -1;

			using (var md5 = MD5.Create())
			{
				Console.WriteLine($"MD5 Checksum: {BitConverter.ToString(md5.ComputeHash(sourceStream)).Replace("-", string.Empty)}");
				sourceStream.Seek(0, SeekOrigin.Begin);
			}

			var sw = Stopwatch.StartNew();

			// Locate and read the package manifest to read the package dependencies
			PackageManifest manifest = PackageReader.ReadManifest(sourceStream);

			if (manifest == null)
			{
				// There was no manifest
				Console.WriteLine($"Cannot load/test a FHIR Implementation Guide Package without a valid manifest  (package.json)");
				return -1;
			}

			Console.WriteLine();

			// Select the version of the processor to use
			Common_Processor versionAgnosticProcessor = null;
			ExpressionValidator expressionValidator = null;
			FHIRVersion? fhirVersion = null;
			var versionInPackage = manifest.GetFhirVersion();
			if (versionInPackage.StartsWith(FHIRVersion.N4_0.GetLiteral()))
			{
				fhirVersion = EnumUtility.ParseLiteral<FHIRVersion>(r4.Hl7.Fhir.Model.ModelInfo.Version);
				versionAgnosticProcessor = new R4_Processor();
				expressionValidator = new ExpressionValidatorR4(versionAgnosticProcessor, settings.ValidateQuestionnaires);
			}
			else if (versionInPackage.StartsWith(FHIRVersion.N4_3.GetLiteral()))
			{
				fhirVersion = EnumUtility.ParseLiteral<FHIRVersion>(r4b.Hl7.Fhir.Model.ModelInfo.Version);
				versionAgnosticProcessor = new R4B_Processor();
				expressionValidator = new ExpressionValidatorR4B(versionAgnosticProcessor, settings.ValidateQuestionnaires);
			}
			else if (versionInPackage.StartsWith(FHIRVersion.N5_0.GetLiteral()))
			{
				fhirVersion = EnumUtility.ParseLiteral<FHIRVersion>(r5.Hl7.Fhir.Model.ModelInfo.Version);
				versionAgnosticProcessor = new R5_Processor();
				expressionValidator = new ExpressionValidatorR5(versionAgnosticProcessor, settings.ValidateQuestionnaires);
			}
			else
			{
				Console.Error.WriteLine($"Unsupported FHIR version: {manifest.GetFhirVersion()} from {string.Join(',', manifest.FhirVersions)}");
				return -1;
			}
			if (manifest.FhirVersions?.Count > 1 || manifest.FhirVersionList?.Count > 1)
				Console.WriteLine($"Detected FHIR Version {versionInPackage} from {string.Join(',', manifest.FhirVersions)} - using {fhirVersion.GetLiteral()}");
			else
				Console.WriteLine($"Detected FHIR Version {versionInPackage} - using {fhirVersion.GetLiteral()}");
			Console.WriteLine();

			// Stash output data
			dumpOutput.title = manifest.Title;
			dumpOutput.fhirVersion = fhirVersion.GetLiteral();
			dumpOutput.version = manifest.Version.ToString();
			if (manifest.Dependencies != null)
			{
				foreach (var item in manifest.Dependencies)
				{
					dumpOutput.dependencies.Add(item.Key, item.Value);
				}
			}

			// Load all the package details (via indexes only) into memory (including dependencies)
			ConsoleEx.WriteLine(ConsoleColor.White, "Package dependencies:");
			var packageCache = new TempPackageCache();
			packageCache.RegisterPackage(manifest.Name, manifest.Version, sourceStream);
			var pd = PackageReader.ReadPackageIndexDetails(sourceStream, packageCache, settings.IgnorePackages);
            PackageReader.ReadAdditionalPackageIndexDetails(pd, settings.AdditionalPackages, packageCache, settings.IgnorePackages);
            var depChecker = new DependencyChecker(settings, fhirVersion.Value, versionAgnosticProcessor.ModelInspector, packageCache);

			// Validate the settings files to skip (ensuring that there are no files that are not in the package)
			ValidateFileInclusionAndExclusionSettings(settings, pd);

			var errs = new List<String>();
			var errFiles = new List<String>();

			// Server to upload the resources to
			BaseFhirClient clientFhir = PrepareTargetFhirClient(settings, versionAgnosticProcessor);

			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, $"Scanning package {manifest.Name} content:");
			// read the root package content
			List<Resource> resourcesFromMainPackage = await depChecker.ReadResourcesFromPackage(pd, (name) => SkipFile(settings, name), sourceStream, versionAgnosticProcessor, errs, errFiles, settings.Verbose, settings.ResourceTypes);

			// Scan through the resources and resolve any canonicals
			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, "Scanning dependencies:");
			depChecker.LoadDependentResources(pd, versionAgnosticProcessor, errFiles);
			var externalCanonicals = pd.RequiresCanonicals.ToList();

			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, "Re-scanning for remaining dependencies:");
			// This set of canonicals were not able to be resolved in the scope of their included package
			// however may have a dependency introduced elsewhere in the dependency tree
			// these ones probably shouldn't be marked with versioned canonicals
			// This will use the "most recent" version if there are multiple that will resolve.
			var allUnresolvedCanonicals = depChecker.UnresolvedCanonicals(pd).ToList();
			foreach (var canonicalUrl in allUnresolvedCanonicals.ToArray())
			{
				// is this canonical in the list of resources....
				var matches = depChecker.ResolveCanonical(pd, canonicalUrl, versionAgnosticProcessor, errFiles);
				var useResource = CurrentCanonicalFromPackages.Current(matches);
				if (useResource != null)
				{
					var distinctVersionSources = matches.Select(m => ResourcePackageSource.PackageSourceVersion(m.resource as IVersionableConformanceResource)).Distinct();
					if (distinctVersionSources.Count() > 1 && settings.Verbose)
					{
						Console.Write($"    Resolved {canonicalUrl.Canonical}|{canonicalUrl.Version} with ");
						ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
						Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
					}
					canonicalUrl.resource = useResource.resource as Resource;
                    useResource.MarkUsedBy(canonicalUrl);
                    allUnresolvedCanonicals.Remove(canonicalUrl);
				}
			}


			// We grab a list of ALL the search parameters we come across to process them at the end - as composites need cross validation
			// this also loads additional resources that are dependencies of the search parameters
			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, "Scanning fhirpath expression dependencies and validating content:");
			expressionValidator.PreValidation(pd, depChecker, settings.Verbose, errFiles);

			foreach (var resource in resourcesFromMainPackage)
			{
				var exampleName = resource.Annotation<ResourcePackageSource>()?.Filename ?? $"Registry {resource.TypeName}/{resource.Id}";
				try
				{
					expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles);
				}
				catch (Exception ex)
				{
					ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: ({exampleName}) {ex.Message}");
					System.Threading.Interlocked.Increment(ref failures);
					// DebugDumpOutputXml(resource);
					errFiles.Add(exampleName);
				}
			}

			// This is the set of resources in the entire processing pack
			var allResources = depChecker.AllResources(pd).Distinct(new ResourcePackageSourceComparer()).ToList();
			var dependencyResourcesToLoad = allResources.Where(r => !resourcesFromMainPackage.Contains(r)).ToList();
			List<Resource> dupCanonicals = new List<Resource>();
			// Remove duplicate canonicals from the content
			// (These are resources where the version was the same in multiple packages)
			// Using CurrentCanonicalFromPackages
			foreach (var item in dependencyResourcesToLoad)
			{
				if (item is IVersionableConformanceResource ivr)
				{
					var matches = dependencyResourcesToLoad.OfType<IVersionableConformanceResource>().Where(t => t.Url == ivr.Url && t.Version == ivr.Version);
					if (matches.Count() > 1)
					{
						var useResource = CurrentCanonicalFromPackages.Current(matches);
						if (item != useResource)
							dupCanonicals.Add(item);
					}
				}
			}
			dependencyResourcesToLoad.RemoveAll(m => dupCanonicals.Contains(m));

			if (settings.ValidateReferencedDependencies)
			{
				foreach (var resource in dependencyResourcesToLoad)
				{
					var att = (resource as Resource).Annotation<ResourcePackageSource>();
					string exampleName;
					if (att.Filename != null)
						exampleName = $"{att.Filename} in {att.PackageId}|{att.PackageVersion}";
					else
						exampleName = $"Registry {resource.TypeName}/{resource.Id}";

					try
					{
							expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles);
						}
						catch (Exception ex)
						{
							ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: ({exampleName}) {ex.Message}");
							System.Threading.Interlocked.Increment(ref failures);
							// DebugDumpOutputXml(resource);
							errFiles.Add(exampleName);
						}
				}
			}

			foreach (var resource in allResources)
			{
				var packageSource = resource.Annotation<ResourcePackageSource>();
				if (packageSource != null)
				{
					expressionValidator.PatchKnownIssues(packageSource.PackageId, packageSource.PackageVersion, resource);
				}
			}

			var registryCanonicals = new List<CanonicalDetails>();

			// Check for missing canonicals on the registry
			List<Resource> additionalResourcesFromRegistry = await ScanExternalRegistry(settings, versionAgnosticProcessor, depChecker, externalCanonicals, allUnresolvedCanonicals, registryCanonicals);
			dependencyResourcesToLoad.InsertRange(0, additionalResourcesFromRegistry);

			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, "Package Processing Summary:");
			pd.DebugToConsole();

			if (settings.PatchCanonicalVersions)
			{
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ConsoleEx.WriteLine(ConsoleColor.White, "Patch Canonical Versions:");
				depChecker.PatchCanonicals(pd);

				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ConsoleEx.WriteLine(ConsoleColor.White, "Package Processing Summary (patched):");
				pd.DebugToConsole(includeContent: false);
			}


			// If loading into a server, report any unresolvable canonicals
			if (!settings.TestPackageOnly)
			{
				Console.WriteLine();
				if (settings.Verbose)
				{
					ReportDependentCanonicalResourcesToConsole(settings, dependencyResourcesToLoad.Where(r => !registryCanonicals.Any(c => c.resource == r)));
					Console.WriteLine();
					if (registryCanonicals.Any())
					{
						ReportRegistryCanonicalResourcesToConsole(settings, registryCanonicals);
						Console.WriteLine();
					}
				}
				ReportUnresolvedCanonicalResourcesToConsole(settings, allUnresolvedCanonicals);
			}

			// Validate/upload the dependent resources
			if (settings.IncludeReferencedDependencies || settings.ValidateReferencedDependencies)
			{
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ConsoleEx.WriteLine(ConsoleColor.White, "Validate/upload dependencies:");
				foreach (var resource in dependencyResourcesToLoad)
				{
					var exampleName = resource.Annotation<ResourcePackageSource>()?.Filename ?? $"Registry {resource.TypeName}/{resource.Id}";
					try
					{
						if (settings.StripNarratives)
						{
							if (resource is DomainResource drStripNarrative)
							{
								drStripNarrative.Text = null;
							}
						}
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

							if (resource is StructureDefinition sd)
							{
								if (settings.ReGenerateSnapshots || settings.RemoveSnapshots) sd.Snapshot = null;
								if (settings.ReGenerateSnapshots || settings.GenerateSnapshots && sd.HasSnapshot == false)
								{
									if (settings.Verbose || !settings.ReGenerateSnapshots)
										Console.WriteLine($"    ----> Generating snapshot for {exampleName}");
									SnapshotGenerator sg = new SnapshotGenerator(expressionValidator.Source);
									await sg.UpdateAsync(sd);
								}
							}
						}

						//if (settings.ValidateReferencedDependencies && !expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles))
						//	continue;

						// Add the file to the output bundle
						IncludeResourceInOutputBundle(settings, alternativeOutputBundle, resource);

						if (!settings.TestPackageOnly && settings.IncludeReferencedDependencies && !string.IsNullOrEmpty(settings.DestinationServerAddress))
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

			// Validate the terminologies to see if we need to pre-expand any of them
			// Now run through all the ValueSets and see if they are simple or complex
			await PerformValueSetPreExpansion(settings, versionAgnosticProcessor, expressionValidator, resourcesFromMainPackage, additionalResourcesFromRegistry).ConfigureAwait(false);

			// Validate/upload the resources
			Console.WriteLine();
			ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
			ConsoleEx.WriteLine(ConsoleColor.White, "Validate/upload package content:");
			foreach (var resource in resourcesFromMainPackage)
			{
				var exampleName = resource.Annotation<ResourcePackageSource>()?.Filename ?? $"Registry {resource.TypeName}/{resource.Id}";
				try
				{
					if (settings.StripNarratives)
					{
						if (resource is DomainResource drStripNarrative)
						{
							drStripNarrative.Text = null;
						}
					}
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

						if (resource is StructureDefinition sd)
						{
							if (settings.ReGenerateSnapshots) sd.Snapshot = null;
							if (settings.ReGenerateSnapshots || settings.GenerateSnapshots && sd.HasSnapshot == false)
							{
								if (settings.Verbose || !settings.ReGenerateSnapshots)
									Console.WriteLine($"    ----> Generating snapshot for {exampleName}");
								SnapshotGenerator sg = new SnapshotGenerator(expressionValidator.Source);
								await sg.UpdateAsync(sd);
							}
						}
					}

					//if (!expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles))
					//	continue;

					// Add the file to the output bundle
					IncludeResourceInOutputBundle(settings, alternativeOutputBundle, resource);

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


			// Ensure that all direct and indirect canonical resources (excluding core spec/extensions) are installed in the server
			if (!settings.TestPackageOnly)
				DependencyChecker.VerifyDependenciesOnServer(settings, clientFhir, externalCanonicals);

			sw.Stop();
			Console.WriteLine("Done!");
			Console.WriteLine();

			if (errs.Any() || errFiles.Any())
			{
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				Console.WriteLine(String.Join("\r\n", errs));
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				Console.WriteLine(String.Join("\r\n", errFiles));
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				Console.WriteLine();
			}
			if (settings.TestPackageOnly)
			{
				// A canonical resource review table
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				Console.WriteLine($"Package Canonical content summary: {resourcesFromMainPackage.Count}");
				Console.WriteLine("\tCanonical Url\tCanonical Version\tStatus\tName");
				foreach (var resource in resourcesFromMainPackage.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
				{
					Console.WriteLine($"\t{resource.Url}\t{resource.Version}\t{resource.Status}\t{resource.Name}");
				}

				// Dependent Canonical Resources
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ReportDependentCanonicalResourcesToConsole(settings, dependencyResourcesToLoad.Where(r => !registryCanonicals.Any(c => c.resource == r)));

				// Registry sourced Canonical Resources
				if (registryCanonicals.Any())
				{
					Console.WriteLine();
					ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
					ReportRegistryCanonicalResourcesToConsole(settings, registryCanonicals);
				}

				// Unresolvable Canonical Resources
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ReportUnresolvedCanonicalResourcesToConsole(settings, allUnresolvedCanonicals);

				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				Console.WriteLine("Package Resource type summary:");
				Console.WriteLine("\tType\tCount");
				foreach (var resource in resourcesFromMainPackage.GroupBy(f => f.TypeName).OrderBy(f => f.Key))
				{
					Console.WriteLine($"\t{resource.Key}\t{resource.Count()}");
				}
				Console.WriteLine($"\tTotal\t{resourcesFromMainPackage.Count()}");
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");

				// And the summary at the end
				Console.WriteLine();
				Console.WriteLine($"Checked: {successes}");
				Console.WriteLine($"Validation Errors: {validationErrors}");
			}
			else
			{
				Console.WriteLine("Package Resource type summary:");
				Console.WriteLine("\tType\tCount");
				foreach (var resource in resourcesFromMainPackage.GroupBy(f => f.TypeName).OrderBy(f => f.Key))
				{
					Console.WriteLine($"\t{resource.Key}\t{resource.Count()}");
				}
				Console.WriteLine($"\tTotal\t{resourcesFromMainPackage.Count()}");
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");

				// And the summary at the end
				Console.WriteLine($"Success: {successes}");
				Console.WriteLine($"Failures: {failures}");
				Console.WriteLine($"Validation Errors: {validationErrors}");
				Console.WriteLine($"Duration: {sw.Elapsed.ToString()}");
				Console.WriteLine($"rps: {(successes + failures) / sw.Elapsed.TotalSeconds}");
			}

			await WriteOutputBundleFile(settings, alternativeOutputBundle, manifest, versionAgnosticProcessor, allUnresolvedCanonicals);

			if (!string.IsNullOrEmpty(settings.OutputDependenciesFile))
			{
				foreach (var resource in resourcesFromMainPackage.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
				{
					dumpOutput.containedCanonicals.Add(new CanonicalDetails()
					{
						ResourceType = (resource as Resource).TypeName,
						Canonical = resource.Url,
						Version = resource.Version,
						Status = resource.Status.GetLiteral(),
						Name = resource.Name,
					});
					// Console.WriteLine($"\t{resource.Url}\t{resource.Version}\t{resource.Status}\t{resource.Name}");
				}
				dumpOutput.externalCanonicalsRequired.AddRange(
					externalCanonicals.Select(rc => new DependentResource()
					{
						resourceType = rc.ResourceType,
						canonical = rc.Canonical,
						version = rc.Version,
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

		private static async Task WriteOutputBundleFile(Settings settings, Bundle alternativeOutputBundle, PackageManifest manifest, Common_Processor versionAgnosticProcessor, List<CanonicalDetails> allUnresolvedCanonicals)
		{
			var filename = settings.OutputBundle;
			if (!string.IsNullOrEmpty(filename))
			{
				// Secret package output processor if the filename ends with .tgz
				if (filename.EndsWith(".tgz"))
				{
					// Write a tgz package file with all the content in it
					var fs = new FileStream(filename, FileMode.Create);
					using (fs)
					{
						Stream gzipStream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Compress, true);
						TarWriter writer = new TarWriter(gzipStream);
						using (writer)
						{
							// write the modified manifest and index
							if (manifest != null)
							{
								// tweak the manifest
								string raw = PackageParser.SerializeManifest(manifest);
								var modifiedManifest = PackageParser.ParseManifest(raw);
								modifiedManifest.Description = "IG repacked with all dependencies inside - " + modifiedManifest.Description;
								if (modifiedManifest.Keywords == null)
									modifiedManifest.Keywords = new List<string>();
								modifiedManifest.Keywords.Add("repacked");
								modifiedManifest.Dependencies = null; // specifically remove the dependencies (as they are now inside)
								modifiedManifest.Directories = null; // specifically remove the directories (as not to spec anyway)
								string jsonManifest = PackageParser.SerializeManifest(modifiedManifest);
								WriteContentToTgz(writer, "package/package.json", jsonManifest);
							}

							// Write all the files into the project
							PackageIndex newIndex = new PackageIndex();
							newIndex.Files = new List<FileDetail>();
							newIndex.indexVersion = 2;
							foreach (var resource in alternativeOutputBundle.Entry.Select(e => e.Resource).OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}").OfType<Resource>())
							{
								var sourceDetails = resource.Annotation<ResourcePackageSource>();
								string folderName = "unknown";
								if (sourceDetails != null)
									folderName = $"{sourceDetails.PackageId}|{sourceDetails.PackageVersion}";
								if (sourceDetails.PackageId == manifest.Name && sourceDetails.PackageVersion == manifest.Version)
									folderName = "package";
								var exampleName = sourceDetails?.Filename ?? resource.Annotation<ResourcePackageSource>()?.Filename ?? $"{resource.TypeName}/{resource.Id}";
								if (exampleName.StartsWith("package/"))
									exampleName = exampleName.Substring(8);
								var entryFilename = $"{folderName}/{exampleName}";
								await WriteContentToTgz(writer, entryFilename, resource, versionAgnosticProcessor);

								newIndex.Files.Add(new FileDetail()
								{
									filename = entryFilename,
									resourceType = resource.TypeName,
									id = resource.Id,
									url = (resource as IVersionableConformanceResource)?.Url,
									version = (resource as IVersionableConformanceResource)?.Version,
								});
							}

							// Write the index
							string jsonIndex = System.Text.Json.JsonSerializer.Serialize<PackageIndex>(newIndex);
							WriteContentToTgz(writer, "package/.index.json", jsonIndex);
						}
					}
				}
				else
				{
					// Write the output bundle to a file
					alternativeOutputBundle.Total = alternativeOutputBundle.Entry.Count;
					ReOrderBundleEntries(alternativeOutputBundle, allUnresolvedCanonicals);
					var fs = new FileStream(filename, FileMode.Create);
					using (fs)
					{
						await versionAgnosticProcessor.SerializeJson(fs, alternativeOutputBundle);
					}
				}
			}
		}

		public static void ReOrderBundleEntries(Bundle alternativeOutputBundle, List<CanonicalDetails> allUnresolvedCanonicals)
		{
			// Scan over the entry list and ensure order them so that any dependencies are before the resources that depend on them
			Queue<Bundle.EntryComponent> entries = new Queue<Bundle.EntryComponent>(alternativeOutputBundle.Entry);
			var reOrderedList = new List<Bundle.EntryComponent>();
			var definedCanonicals = new StringCollection();
			definedCanonicals.AddRange(allUnresolvedCanonicals.Select(c => c.Canonical).ToArray());
			definedCanonicals.AddRange(allUnresolvedCanonicals.Select(c => $"{c.Canonical}|{c.Version}").ToArray());
			var futureCanonicals = new Dictionary<string, Resource>();
			var lastEntry = alternativeOutputBundle.Entry.LastOrDefault();
			while (entries.Any())
			{
				var entry = entries.Dequeue();

				var ivr = entry.Resource as IVersionableConformanceResource;
				var versionedCanonical = $"{ivr?.Url}|{ivr?.Version}";
				var unresolvedDeps = entry.Resource.Annotations<DependsOnCanonical>().Where(d => !definedCanonicals.Contains(d.CanonicalUrl) && d.CanonicalUrl != ivr.Url && d.CanonicalUrl != versionedCanonical);

				// Reset end of queue indicator, which means all canonicals have been found.
				if (entry == lastEntry)
					lastEntry = null;

				if (!unresolvedDeps.Any())
				{
					System.Diagnostics.Trace.WriteLine($"Included {versionedCanonical}");
					reOrderedList.Add(entry);
					if (ivr != null)
					{
						if (!definedCanonicals.Contains(ivr.Url))
							definedCanonicals.Add(ivr.Url);
						definedCanonicals.Add(versionedCanonical);
						if (futureCanonicals.ContainsKey(ivr.Url))
							futureCanonicals.Remove(ivr.Url);
						if (futureCanonicals.ContainsKey(versionedCanonical))
							futureCanonicals.Remove(versionedCanonical);
					}
					continue;
				}

				if (ivr != null)
				{
					if (!futureCanonicals.ContainsKey(ivr.Url))
						futureCanonicals.Add(ivr.Url, entry.Resource);
					if (!futureCanonicals.ContainsKey(versionedCanonical))
						futureCanonicals.Add(versionedCanonical, entry.Resource);
				}

				// Check to see if all the deps are coming
				if (lastEntry == null)
				{
					var missingDeps = unresolvedDeps.Where(d => !futureCanonicals.ContainsKey(d.CanonicalUrl)).ToList();
					if (missingDeps.Any())
					{
						System.Diagnostics.Trace.WriteLine($"    unknown deps {versionedCanonical}: missing {string.Join(", ", missingDeps.Select(d => d.CanonicalUrl))}");
						// This could be due to errors during processing, so lets assume they are "just missing"
						definedCanonicals.AddRange(missingDeps.Select(d => d.CanonicalUrl).ToArray());
					}
					else
					{
						// This is the area where we may have circular dependencies being detected

						// Check for circular dependencies to break
						var nonCircularDeps = unresolvedDeps.Where(d => !HasCircularDependency(entry.Resource, d.CanonicalUrl, futureCanonicals, definedCanonicals));
						if (!nonCircularDeps.Any())
						{
							// This is all circular dependencies, so lets add it anyway
							System.Diagnostics.Trace.WriteLine($"Included {versionedCanonical} (ignoring circular deps: {string.Join(", ", unresolvedDeps.Select(d => d.CanonicalUrl))})");
							reOrderedList.Add(entry);
							if (ivr != null)
							{
								if (!definedCanonicals.Contains(ivr.Url))
									definedCanonicals.Add(ivr.Url);
								definedCanonicals.Add(versionedCanonical);
								if (futureCanonicals.ContainsKey(ivr.Url))
									futureCanonicals.Remove(ivr.Url);
								if (futureCanonicals.ContainsKey(versionedCanonical))
									futureCanonicals.Remove(versionedCanonical);
							}
							continue;
						}
						// wasn't found, so defer it again
						System.Diagnostics.Trace.WriteLine($"* Deferring {versionedCanonical}: needs {string.Join(", ", unresolvedDeps.Select(d => d.CanonicalUrl))}");
					}
				}
				else
				{
					System.Diagnostics.Trace.WriteLine($"Deferring {versionedCanonical}: needs {string.Join(", ", unresolvedDeps.Select(d => d.CanonicalUrl))}");
				}

				// else, add it back to the end of the queue
				entries.Enqueue(entry);
			}

			// swap out the entry list
			alternativeOutputBundle.Entry = reOrderedList;
		}

		public static bool HasCircularDependency(Resource resource, string canonicalUrl, Dictionary<string, Resource> futureCanonicals, StringCollection skipCanonicals)
		{
			var allDependencies = new List<Resource>();
			allDependencies.Add(resource);

			var ivr = resource as IVersionableConformanceResource;
			var versionedCanonical = $"{ivr?.Url}|{ivr?.Version}";
			var unresolvedDeps = resource.Annotations<DependsOnCanonical>().Where(d => !skipCanonicals.Contains(d.CanonicalUrl) && d.CanonicalUrl != ivr.Url && d.CanonicalUrl != versionedCanonical);

			foreach (var dep in unresolvedDeps)
			{
				if (futureCanonicals.TryGetValue(dep.CanonicalUrl, out Resource depResource))
				{
					if (canonicalUrl != dep.CanonicalUrl)
						continue;
					var cr = HasCircularDependencyOn(ivr, depResource, futureCanonicals, skipCanonicals, allDependencies);
					if (cr == true)
						return true;
				}
			}

			return false;
		}

		public static bool? HasCircularDependencyOn(IVersionableConformanceResource ivrRoot, Resource resource, Dictionary<string, Resource> futureCanonicals, StringCollection skipCanonicals, List<Resource> allDependencies)
		{
			if (allDependencies.Contains(resource))
				return null; // This was a circular dependency, however not via the ivrRoot - so can't declare anything

			allDependencies.Add(resource);

			var ivr = resource as IVersionableConformanceResource;
			var versionedCanonical = $"{ivr?.Url}|{ivr?.Version}";
			var unresolvedDeps = resource.Annotations<DependsOnCanonical>().Where(d => !skipCanonicals.Contains(d.CanonicalUrl) && d.CanonicalUrl != ivr.Url && d.CanonicalUrl != versionedCanonical);

			foreach (var dep in unresolvedDeps)
			{
				if (dep.CanonicalUrl == ivrRoot.Url || dep.CanonicalUrl == $"{ivrRoot.Url}|{ivrRoot.Version}")
					return true;
				if (futureCanonicals.TryGetValue(dep.CanonicalUrl, out Resource depResource))
				{
					if (allDependencies.Contains(depResource))
						continue;
					if (HasCircularDependencyOn(ivrRoot, depResource, futureCanonicals, skipCanonicals, allDependencies) == true)
						return true;
				}
			}

			return false;
		}

		private static void WriteContentToTgz(TarWriter writer, string filename, string content)
		{
			var entry = new PaxTarEntry(TarEntryType.RegularFile, filename);
			var bytes = Encoding.UTF8.GetBytes(content);
			entry.DataStream = new MemoryStream(bytes);
			writer.WriteEntry(entry);
		}

		private static async Task WriteContentToTgz(TarWriter writer, string filename, Resource resource, Common_Processor versionAgnosticProcessor)
		{
			var ms = new MemoryStream();
			using (ms)
			{
				await versionAgnosticProcessor.SerializeJson(ms, resource);
				ms.Seek(0, SeekOrigin.Begin);
				var entry = new PaxTarEntry(TarEntryType.RegularFile, filename);
				entry.DataStream = ms;
				await writer.WriteEntryAsync(entry);
			}
		}

		private static async Task PerformValueSetPreExpansion(Settings settings, Common_Processor versionAgnosticProcessor, ExpressionValidator expressionValidator, List<Resource> resourcesToProcess, List<Resource> additionalResources)
		{
			if (!string.IsNullOrEmpty(settings.ExternalTerminologyServer))
			{
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ConsoleEx.WriteLine(ConsoleColor.White, $"Validate ValueSet complexity (and pre-expand if possible using {settings.ExternalTerminologyServer}):");
				var valueSets = resourcesToProcess.OfType<ValueSet>();
				var codeSystems = resourcesToProcess.OfType<CodeSystem>();

				var expanderSettings = ValueSetExpanderSettings.CreateDefault();
				expanderSettings.ValueSetSource = expressionValidator.Source;
				var _expander = new ValueSetExpander(expanderSettings);

				var tsClient = new BaseFhirClient(new Uri(settings.ExternalTerminologyServer), versionAgnosticProcessor.ModelInspector);
				BaseFhirClient tsRegistry = null;
				if (additionalResources.Count > 0)
					tsRegistry = new BaseFhirClient(new Uri(settings.ExternalRegistry), versionAgnosticProcessor.ModelInspector);

				StringCollection failures = new StringCollection();
				int countSuccesses = 0;

				foreach (var vs in valueSets)
				{
					if (!vs.HasExpansion)
					{
						// Clone the ValueSet so that we can expand it without affecting the original
						var vsCopy = (ValueSet)vs.DeepCopy();
						try
						{
							await _expander.ExpandAsync(vsCopy).ConfigureAwait(false);
							// Console.WriteLine($"ValueSet {vs.Url} expanded");
							countSuccesses++;
						}
						catch (TerminologyServiceException ex)
						{
							// Console.WriteLine($"ValueSet {vs.Url} failed to expand: {ex.Message}");

							// Need to expand this one with the terminology service
							try
							{
								ValueSet expandedValueSet;
								if (additionalResources.Contains(vs))
									expandedValueSet = tsRegistry.ExpandValueSet(vs);
								else
									expandedValueSet = tsClient.ExpandValueSet(vs);
								if (expandedValueSet.Expansion == null)
								{
									ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    ValueSet {vs.Url} failed to expand");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Contains.Count() > (settings.MaxExpansionSize ?? 1000))
								{
									ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    ValueSet {vs.Url} expansion is too large to include ({expandedValueSet.Expansion.Contains.Count()} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Total.HasValue && expandedValueSet.Expansion.Contains.Count() != expandedValueSet.Expansion.Total.Value)
								{
									ConsoleEx.WriteLine(ConsoleColor.Yellow, $"   ValueSet {vs.Url} expansion is incomplete ({expandedValueSet.Expansion.Contains.Count()} of {expandedValueSet.Expansion.Total} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Total.HasValue && expandedValueSet.Expansion.Total.Value > (settings.MaxExpansionSize ?? 1000))
								{
									ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    ValueSet {vs.Url} expansion is too large to include ({expandedValueSet.Expansion.Total} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								// flag for limited expansion too?

								if (expandedValueSet.Expansion.NextElement != null)
								{
									ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    ValueSet {vs.Url} expansion is too large to include");
									failures.Add(vs.Url);
									continue;
								}
								// Yay! we have an expansion we can use, so set it
								Console.WriteLine($"    ValueSet {vs.Url} expansion included ({expandedValueSet.Expansion.Contains.Count()} concepts)");
								vs.Expansion = expandedValueSet.Expansion;
							}
							catch (Hl7.Fhir.Rest.FhirOperationException exExpand)
							{
								Console.WriteLine($"    ValueSet {vs.Url} failed to expand on {settings.ExternalTerminologyServer}");
								Console.WriteLine($"      * pre-expansion required due to: {ex.Message}");

								if (exExpand.Outcome != null)
								{
									foreach (var issue in exExpand.Outcome.Issue)
									{
										if (issue.Severity != OperationOutcome.IssueSeverity.Information)
											Console.WriteLine($"      * {issue.Severity} {issue.Code} {issue.Details?.Text ?? issue.Details?.Coding.FirstOrDefault()?.Display}");
									}
								}
								else
								{
									Console.WriteLine($"      * {exExpand.Message}");
								}
							}
							catch (Exception exExpand)
							{
								ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    ValueSet {vs.Url} failed to expand on {settings.ExternalTerminologyServer}: {exExpand.Message}");
							}
						}
					}
					else
					{
						// Console.WriteLine($"ValueSet {vs.Url} already expanded");
					}
				}
				// Assert.AreEqual(35, countSuccesses, "ValueSet expansions");
				// Assert.AreEqual(0, failures.Count, $"Failed to expand {failures.Count} ValueSets");
			}
		}

		private static async Task<List<Resource>> ScanExternalRegistry(Settings settings, Common_Processor versionAgnosticProcessor, DependencyChecker depChecker, List<CanonicalDetails> externalCanonicals, List<CanonicalDetails> unresolvableCanonicals, List<CanonicalDetails> indirectCanonicals)
		{
			List<Resource> additionalResources = new List<Resource>();
			if (!string.IsNullOrEmpty(settings.ExternalRegistry))
			{
				Console.WriteLine();
				ConsoleEx.WriteLine(ConsoleColor.White, "--------------------------------------");
				ConsoleEx.WriteLine(ConsoleColor.White, $"Scanning external registry:\r\n\t{settings.ExternalRegistry}");
				BaseFhirClient clientRegistry = null;

				// Need to pass through the destination header too
				HttpClient client = new HttpClient();
				if (settings.ExternalRegistryHeaders?.Any() == true)
				{
					foreach (var header in settings.ExternalRegistryHeaders)
					{
						if (header.Contains(":"))
						{
							var kv = header.Split(new char[] { ':' }, 2);
							client.DefaultRequestHeaders.Add(kv[0].Trim(), kv[1].Trim());
						}
					}
				}
				clientRegistry = new BaseFhirClient(new Uri(settings.ExternalRegistry), client, versionAgnosticProcessor.ModelInspector);
				if (settings.DestinationFormat == upload_format.json)
					clientRegistry.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Json;
				if (settings.DestinationFormat == upload_format.xml)
					clientRegistry.Settings.PreferredFormat = Hl7.Fhir.Rest.ResourceFormat.Xml;
				clientRegistry.Settings.VerifyFhirVersion = false;

				// Check that the resources are available on the external registry
				foreach (var dc in unresolvableCanonicals.ToArray()) // clone the list so that we can trim it down it while processing
				{
					try
					{
						if (settings.Verbose)
							Console.WriteLine($"Searching registry for {dc.ResourceType} {dc.Canonical}");
						var r = await clientRegistry.SearchAsync(dc.ResourceType, new[] { $"url={dc.Canonical}" }, null, null, Hl7.Fhir.Rest.SummaryType.Data);
						if (r.Entry.Count > 1)
						{
							// Check if these are just more versions of the same thing, then to the canonical versioning thingy
							// to select the latest version.
							var cv = CurrentCanonicalFromPackages.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));

							// remove all the others that aren't current.
							r.Entry.RemoveAll(e => e.Resource != cv as Resource);
						}
						if (r.Entry.Count() == 1)
						{
							var resolvedResource = r.Entry.First().Resource;
							// strip the SUBSETTED tag if it is there as we intentionally asked for data only (no narrative)
							resolvedResource.Meta?.Tag?.RemoveAll(t => t.Code == "SUBSETTED");
							additionalResources.Add(resolvedResource);
							// UploadFile(settings, clientFhir, resolvedResource);
							unresolvableCanonicals.RemoveAll(uc => uc.Canonical == dc.Canonical);
							indirectCanonicals.Add(dc);
							dc.resource = resolvedResource;

							// Add a fake package source entry (indicating which registry)
							dc.resource.SetAnnotation(new ResourcePackageSource()
							{
								PackageId = "registry",
								PackageVersion = settings.ExternalRegistry
							});
						}
						if (!r.Entry.Any())
						{
							// Console.WriteLine($"{dc.resourceType} Canonical {dc.canonical} was not present on the registry");
							// unresolvableCanonicals.Add(dc);
						}
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"Error searching for {dc.ResourceType} {dc.Canonical} at {settings.ExternalRegistry} {ex.Message}");
					}
				}
				// now perform another scan for their dependencies too
				var initialCanonicals = additionalResources.Select(ec => new CanonicalDetails()
				{
					ResourceType = ec.TypeName,
					Canonical = (ec as IVersionableConformanceResource).Url,
					Version = (ec as IVersionableConformanceResource).Version
				}).ToList();
				var dependentCanonicals = depChecker.ScanForCanonicals(initialCanonicals.Union(externalCanonicals), additionalResources);
				foreach (var dc in dependentCanonicals)
				{
					try
					{
						if (settings.Verbose)
							Console.WriteLine($"Searching registry for {dc.ResourceType} {dc.Canonical}");
						var r = clientRegistry.Search(dc.ResourceType, new[] { $"url={dc.Canonical}" }, null, null, Hl7.Fhir.Rest.SummaryType.Data);
						if (r.Entry.Count > 1)
						{
							// Check if these are just more versions of the same thing, then to the canonical versioning thingy
							// to select the latest version.
							var cv = CurrentCanonicalFromPackages.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));
							// remove all the others that aren't current.
							r.Entry.RemoveAll(e => e.Resource != cv as Resource);
						}
						if (r.Entry.Count() == 1)
						{
							var resolvedResource = r.Entry.First().Resource;
							// strip the SUBSETTED tag if it is there as we intentionally asked for data only (no narrative)
							resolvedResource.Meta?.Tag?.RemoveAll(t => t.Code == "SUBSETTED");
							additionalResources.Insert(0, resolvedResource); // put dependencies at the start of the list
																			 // UploadFile(settings, clientFhir, resolvedResource);
							unresolvableCanonicals.RemoveAll(uc => uc.Canonical == dc.Canonical);
							indirectCanonicals.Add(dc);
							dc.resource = resolvedResource;

							// Add a fake package source entry (indicating which registry)
							dc.resource.SetAnnotation(new ResourcePackageSource()
							{
								PackageId = "registry",
								PackageVersion = settings.ExternalRegistry
							});
						}
						if (!r.Entry.Any())
						{
							// Console.WriteLine($"{dc.resourceType} Canonical {dc.canonical} was not present on the registry");
							unresolvableCanonicals.Add(dc);
						}
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"Error searching for {dc.ResourceType} {dc.Canonical} at {settings.ExternalRegistry} {ex.Message}");
					}
				}

				// output a bundle with these additional resources
				if (!string.IsNullOrEmpty(settings.ExternalRegistryExportFile))
				{
					var bundle = new Bundle();
					bundle.Type = Bundle.BundleType.Collection;
					bundle.Entry.AddRange(additionalResources.Select(r => new Bundle.EntryComponent() { Resource = r }));

					var fs = new FileStream(settings.ExternalRegistryExportFile, FileMode.Create);
					using (fs)
					{
						await versionAgnosticProcessor.SerializeJson(fs, bundle);
					}
				}
			}
			return additionalResources;
		}

		private static void ValidateFileInclusionAndExclusionSettings(Settings settings, PackageDetails pd)
		{
			if (settings.IgnoreFiles?.Any() == true)
			{
				var missingFiles = settings.IgnoreFiles.Where(file => !pd.Files.Any(f => f.filename == file)).ToList();
				if (missingFiles.Any())
				{
					Console.WriteLine("The following files to ignore were not found in the package:");
					foreach (var file in missingFiles)
						Console.WriteLine($"    {file}");
				}
			}
			if (settings.SelectFiles?.Any() == true)
			{
				var missingFiles = settings.SelectFiles.Where(file => !pd.Files.Any(f => f.filename == file)).ToList();
				if (missingFiles.Any())
				{
					Console.WriteLine("The following files were not found in the package:");
					foreach (var file in missingFiles)
						Console.WriteLine($"    {file}");
				}
			}
		}

		private static BaseFhirClient PrepareTargetFhirClient(Settings settings, Common_Processor versionAgnosticProcessor)
		{
			BaseFhirClient clientFhir = null;
			if (!string.IsNullOrEmpty(settings.DestinationServerAddress))
			{
				// Need to pass through the destination header too
				HttpClient client = new HttpClient();
				if (Program.useClient != null)
					client = Program.useClient;
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

			return clientFhir;
		}

		private static async Task<Stream> GetSourcePackageStream(Settings settings, OutputDependenciesFile dumpOutput)
        {
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
                    // PackageClient pc = PackageClient.Create("https://packages2.fhir.org/packages");
                    PackageClient pc = PackageClient.Create();
                    examplesPkg = await pc.GetPackage(new PackageReference(settings.PackageId, null));
                    string contents = Encoding.UTF8.GetString(examplesPkg);
                    var pl = JsonConvert.DeserializeObject<PackageListing>(contents);
                    Console.WriteLine($"Package ID: {pl?.Name}");
                    Console.WriteLine($"Package Title: {pl?.Description}");
                    Console.WriteLine($"Available Versions: {String.Join(", ", pl.Versions.Keys)}");
                    if (!string.IsNullOrEmpty(settings.PackageVersion) && !pl.Versions.ContainsKey(settings.PackageVersion))
                    {
                        Console.Error.WriteLine($"Version {settings.PackageVersion} was not in the registered versions");
                        return null;
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
                    Console.WriteLine($"Description: {pl.Versions[settings.PackageVersion].Description.Replace("\n", "\n    ")}");
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
                            client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("UploadFIG", "8.10.1"));
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

            // As our engine needs to rewind the stream, we need to ensure that it can be done
            if (!sourceStream.CanSeek)
            {
                // This stream can't be re-wound, so we need to copy it to a memory stream
                MemoryStream ms = new MemoryStream();
                await sourceStream.CopyToAsync(ms);
                ms.Seek(0, SeekOrigin.Begin);
                sourceStream = ms;
            }
            return sourceStream;
        }

        private static void IncludeResourceInOutputBundle(Settings settings, Bundle alternativeOutputBundle, Resource resource)
        {
			var newEntry = new Bundle.EntryComponent()
			{
				// FullUrl = "urn:uuid:" + Guid.NewGuid().ToString("D"),
				Resource = resource,
				Request = new Bundle.RequestComponent()
				{
					Method = Bundle.HTTPVerb.POST,
					Url = $"{resource.TypeName}",
				},
			};
			alternativeOutputBundle.Entry.Add(newEntry);

			if (!string.IsNullOrEmpty(resource.Id))
			{
				newEntry.Request.Method = Bundle.HTTPVerb.PUT;
				newEntry.Request.Url = $"{resource.TypeName}/{resource.Id}";
			}

			if (resource is IVersionableConformanceResource ivr_resource)
			{
				// Use the Conditional Update format
				// https://build.fhir.org/http.html#cond-update
				// noting that this may update resources that are not intended to be updated if
				// the package has multiple versions of the same canonical,
				// or the destination server has multiple versions
				newEntry.Request.Method = Bundle.HTTPVerb.PUT;
				newEntry.Request.Url = $"{resource.TypeName}?url={ivr_resource.Url}&version={ivr_resource.Version}";
				resource.Id = null;
			}
		}

		private static void ReportDependentCanonicalResourcesToConsole(Settings settings, IEnumerable<Resource> list)
        {
            Console.WriteLine($"Canonical resources from dependency packages: {list.Count()}");
			Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tPackage Source");
			foreach (var details in list.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
			{
				var resource = details as Resource;
				Console.Write($"\t{resource.TypeName}\t{details.Url}\t{details.Version}");
				if (resource.HasAnnotation<ResourcePackageSource>() == true)
				{
					var sourceDetails = resource.Annotation<ResourcePackageSource>();
					Console.Write($"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
				}
				Console.WriteLine();
			}
		}

		private static void ReportCanonicalDetailsToConsole(Settings settings, IEnumerable<CanonicalDetails> list)
		{
			Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tPackage Source");
			foreach (var details in list.OrderBy(f => $"{f.Canonical}|{f.Version}"))
			{
				Console.Write($"\t{details.ResourceType}\t{details.Canonical}\t{details.Version}");
				if (details.resource?.HasAnnotation<ResourcePackageSource>() == true)
				{
					var sourceDetails = details.resource.Annotation<ResourcePackageSource>();
					Console.Write($"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
				}
				Console.WriteLine();
				if (settings.Verbose)
				{
					foreach (var dr in details.requiredBy)
					{
						if (dr is IVersionableConformanceResource cr)
							Console.Write($"\t\t\t\t\t^- {cr.Url}|{cr.Version}");
						else
							Console.Write($"\t\t\t\t\t^- {dr.TypeName}/{dr.Id}");
						if (dr.HasAnnotation<ResourcePackageSource>())
						{
							var sourceDetails = dr.Annotation<ResourcePackageSource>();
							Console.Write($"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
						}
						Console.WriteLine();
					}
				}
			}
		}

		private static void ReportRegistryCanonicalResourcesToConsole(Settings settings, IEnumerable<CanonicalDetails> registryCanonicals)
        {
            Console.WriteLine($"Canonical resources from the registry: {registryCanonicals.Count()}");
			ReportCanonicalDetailsToConsole(settings, registryCanonicals);
        }

        private static void ReportUnresolvedCanonicalResourcesToConsole(Settings settings, IEnumerable<CanonicalDetails> unresolvableCanonicals)
        {
			// Merge all the canonical details into a list de-duplicating the canonical versions and merging the RequiredBy into a single list
			Dictionary<string, CanonicalDetails> mergedCDs = new ();
			foreach (var cd in unresolvableCanonicals)
			{
				var key = $"{cd.Canonical}|{cd.Version}";
				if (!mergedCDs.ContainsKey(key))
				{
					mergedCDs.Add(key, cd);
				}
				else
				{
					var newCd = new CanonicalDetails 
					{
						Canonical = cd.Canonical,
						Name = cd.Name,
						Version = cd.Version,
						ResourceType = cd.ResourceType,
						Status = cd.Status,
					};
					newCd.requiredBy.AddRange(mergedCDs[key].requiredBy);
					foreach (var req in cd.requiredBy)
					{
						if (!newCd.requiredBy.Contains(req))
							newCd.requiredBy.Add(req);
					}
					mergedCDs[key] = newCd;
				}
			}
            Console.WriteLine($"Unable to resolve these canonical resources: {mergedCDs.Values.Count()}");
			ReportCanonicalDetailsToConsole(settings, mergedCDs.Values);
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
						Console.Write($"    {original.TypeName}/{original.Id} unchanged {(resource as IVersionableConformanceResource)?.Version}");
						if (resource.HasAnnotation<ResourcePackageSource>() == true)
						{
							var sourceDetails = resource.Annotation<ResourcePackageSource>();
							Console.Write($"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
						}
						Console.WriteLine();
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
						ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: Canonical {vcs.Url}|{vcs.Version} has multiple instances already loaded - Must be resolved manually as unable to select which to update");
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
							ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: Canonical {vcs.Url} already has other versions loaded - {string.Join(", ", otherCanonicalVersionNumbers)}, can't also load {vcs.Version}, adding may cause issues if the server can't determine which is the latest to use");
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
							if (resource.HasAnnotation<ResourcePackageSource>() == true)
							{
								var sourceDetails = resource.Annotation<ResourcePackageSource>();
								Console.Write($"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
							}
							if (!string.IsNullOrEmpty(warningMessage))
							{
								ConsoleEx.WriteLine(ConsoleColor.Yellow, $"\t{warningMessage}");
							}
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

			if (result is IVersionableConformanceResource r)
				ConsoleEx.Write(ConsoleColor.DarkGreen, $"    {operation}\t{result.TypeName}\t{r.Url}|{r.Version}");
			else
				ConsoleEx.Write(ConsoleColor.DarkGreen, $"    {operation}\t{result.TypeName}/{result.Id} {result.VersionId}");
			if (resource.HasAnnotation<ResourcePackageSource>() == true)
			{
				var sourceDetails = resource.Annotation<ResourcePackageSource>();
				ConsoleEx.Write(ConsoleColor.DarkGreen, $"\t({sourceDetails.PackageId}|{sourceDetails.PackageVersion})");
			}
			if (!string.IsNullOrEmpty(warningMessage))
			{
				ConsoleEx.Write(ConsoleColor.Yellow, $"\t{warningMessage}");
			}
			Console.WriteLine();
			return result;
		}
	}
}
