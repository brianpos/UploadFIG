extern alias r4;
extern alias r4b;
extern alias r5;

using Firely.Fhir.Packages;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Snapshot;
using Hl7.Fhir.Specification.Terminology;
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
			Console.WriteLine("--------------------------------------");

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
				new Option<List<string>>(new string[]{ "-if", "--ignoreFiles" }, () => settings.IgnoreFiles, "Any specific files that should be ignored/skipped when processing the package"),
				new Option<List<string>>(new string[]{ "-ic", "--ignoreCanonicals" }, () => settings.IgnoreCanonicals, "Any specific Canonical URls that should be ignored/skipped when processing the package"),
				destinationServerOption,
				new Option<List<string>>(new string[]{ "-dh", "--destinationServerHeaders"}, () => settings.DestinationServerHeaders, "Headers to add to the request to the destination FHIR Server"),
				new Option<upload_format>(new string[]{ "-df", "--destinationFormat"}, () => settings.DestinationFormat ?? upload_format.xml, "The format to upload to the destination server"),
				testPackageOnlyOption,
				new Option<bool>(new string[] { "-vq", "--validateQuestionnaires" }, () => settings.ValidateQuestionnaires, "Include more extensive testing on Questionnaires (experimental)"),
				new Option<bool>(new string[] { "-vrd", "--validateReferencedDependencies" }, () => settings.ValidateReferencedDependencies, "Validate any referenced resources from dependencies being installed"),
				new Option<bool>(new string[]{ "-pdv", "--preventDuplicateCanonicalVersions"}, () => settings.PreventDuplicateCanonicalVersions, "Permit the tool to upload canonical resources even if they would result in the server having multiple canonical versions of the same resource after it runs\r\nThe requires the server to be able to handle resolving canonical URLs to the correct version of the resource desired by a particular call. Either via the versioned canonical reference, or using the logic defined in the $current-canonical operation"),
				new Option<bool>(new string[]{ "-cn", "--checkAndCleanNarratives"}, () => settings.CheckAndCleanNarratives, "Check and clean any narratives in the package and remove suspect ones\r\n(based on the MS FHIR Server's rules)"),
				new Option<bool>(new string[]{ "-c", "--checkPackageInstallationStateOnly"}, () => settings.CheckPackageInstallationStateOnly, "Download and check the package and compare with the contents of the FHIR Server,\r\n but do not update any of the contents of the FHIR Server"),
				new Option<bool>(new string[]{ "-gs", "--generateSnapshots"}, () => settings.GenerateSnapshots, "Generate the snapshots for any missing snapshots in StructureDefinitions"),
				new Option<bool>(new string[]{ "-rs", "--regenerateSnapshots"}, () => settings.ReGenerateSnapshots, "Re-Generate all snapshots in StructureDefinitions"),
				new Option<bool>(new string[] { "--includeReferencedDependencies" }, () => settings.IncludeReferencedDependencies, "Upload any referenced resources from resource dependencies being included"),
				new Option<bool>(new string[]{ "--includeExamples"}, () => settings.IncludeExamples, "Also include files in the examples sub-directory\r\n(Still needs resource type specified)"),
				new Option<bool>(new string[]{ "--verbose"}, () => settings.Verbose, "Provide verbose diagnostic output while processing\r\n(e.g. Filenames processed)"),
				new Option<string>(new string[] { "-odf", "--outputDependenciesFile" }, () => settings.OutputDependenciesFile, "Write the list of dependencies discovered in the IG into a json file for post-processing"),
				new Option<string>(new string[] { "-reg", "--externalRegistry" }, () => settings.ExternalRegistry, "The URL of an external FHIR server to use for resolving resources not already on the destination server"),
				new Option<List<string>>(new string[] { "-regh", "--externalRegistryHeaders" }, () => settings.ExternalRegistryHeaders, "Additional headers to supply when connecting to the external FHIR server"),
				new Option<string>(new string[] { "-ets", "--externalTerminologyServer" }, () => settings.ExternalTerminologyServer, "The URL of an external FHIR terminology server to use for creating expansions (where not on an external registry)"),
				new Option<List<string>>(new string[] { "-etsh", "--externalTerminologyServerHeaders" }, () => settings.ExternalTerminologyServerHeaders, "Additional headers to supply when connecting to the external FHIR terminology server"),
				new Option<long?>(new string [] { "-mes", "--maxExpansionSize" }, () => settings.MaxExpansionSize, "The maximum number of codes to include in a ValueSet expansion"),
				new Option<string>(new string[] { "-rego", "--ExternalRegistryExportFile" }, () => settings.ExternalRegistryExportFile, "The filename of a file to write the json bundle of downloaded registry resources to"),
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

			Stream gzipStream = new System.IO.Compression.GZipStream(sourceStream, System.IO.Compression.CompressionMode.Decompress);
			MemoryStream ms = new MemoryStream();
			using (gzipStream)
			{
				// Unzip the tar file into a memory stream
				await gzipStream.CopyToAsync(ms);
				ms.Seek(0, SeekOrigin.Begin);
			}
			var reader = new TarReader(ms);

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

			Console.WriteLine("Package dependencies:");
			if (manifest.Dependencies != null)
				Console.WriteLine($"    {string.Join("\r\n    ", manifest.Dependencies.Select(d => $"{d.Key}|{d.Value}"))}");
			else
				Console.WriteLine($"    (none)");


			// skip back to the start (for cases where the package.json isn't the first resource)
			ms.Seek(0, SeekOrigin.Begin);
			reader = new TarReader(ms);

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

			var errs = new List<String>();
			var errFiles = new List<String>();

			// Server to upload the resources to
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

			// Load all the content in so that it can then be re-sequenced
			Console.WriteLine();
			Console.WriteLine("--------------------------------------");
			Console.WriteLine("Scanning package content:");
			List<Resource> resourcesToProcess = ReadResourcesFromPackage(settings, reader, versionAgnosticProcessor, errs, errFiles);

			// Scan through the resources and resolve any direct canonicals
			Console.WriteLine();
			Console.WriteLine("--------------------------------------");
			Console.WriteLine("Scanning dependencies:");
			var requiresDirectCanonicals = DependencyChecker.ScanForCanonicals(resourcesToProcess).ToList();
			var externalDirectCanonicals = DependencyChecker.FilterOutCanonicals(requiresDirectCanonicals, resourcesToProcess).ToList();
			var externalNonCoreDirectCanonicals = DependencyChecker.FilterOutCoreSpecAndExtensionCanonicals(externalDirectCanonicals, fhirVersion.Value, versionAgnosticProcessor).ToList();

			// We grab a list of ALL the search parameters we come across to process them at the end - as composites need cross validation
			expressionValidator.PreValidation(manifest.Dependencies ?? new Dictionary<string, string?>(), resourcesToProcess, settings.Verbose);

			// Locate any indirect canonicals
			Console.WriteLine();
			Console.WriteLine("Scanning indirect dependencies:");
			var indirectCanonicals = DependencyChecker.RecurseDependencies(externalNonCoreDirectCanonicals, expressionValidator.InMemoryResolver, fhirVersion.Value, versionAgnosticProcessor).ToList();
			var unresolvableCanonicals = externalNonCoreDirectCanonicals.Union(indirectCanonicals).Where(ic => ic.resource == null).ToList();
			var dependencyResourcesToLoad = externalNonCoreDirectCanonicals.Union(indirectCanonicals).Where(ic => ic.resource != null).Select(ic => ic.resource).ToList();

			// Check for missing canonicals on the registry
			List<Resource> additionalResources = new List<Resource>();
			if (!string.IsNullOrEmpty(settings.ExternalRegistry))
			{
				Console.WriteLine();
				Console.WriteLine($"Scanning external registry:\r\n\t{settings.ExternalRegistry}");
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
				PackageCacheItem registryCacheItemFake = new PackageCacheItem() 
				{
					packageId = "registry",
					packageVersion = settings.ExternalRegistry,
				};
				foreach (var dc in unresolvableCanonicals.ToArray()) // clone the list so that we can trim it down it while processing
				{
					try
					{
						if (settings.Verbose)
							Console.WriteLine($"Searching registry for {dc.resourceType} {dc.canonical}");
						var r = await clientRegistry.SearchAsync(dc.resourceType, new[] { $"url={dc.canonical}" }, null, null, Hl7.Fhir.Rest.SummaryType.Data);
						if (r.Entry.Count > 1)
						{
							// Check if these are just more versions of the same thing, then to the canonical versioning thingy
							// to select the latest version.
							var cv = Hl7.Fhir.WebApi.CurrentCanonical.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));

							// remove all the others that aren't current.
							r.Entry.RemoveAll(e => e.Resource != cv as Resource);
						}
						if (r.Entry.Count() == 1)
						{
							var resolvedResource = r.Entry.First().Resource;
							// strip the SUBSETTED tag if it is there as we intentionally asked for data only (no narrative)
							resolvedResource.Meta?.Tag?.RemoveAll(t => t.Code == "SUBSETTED");
							resolvedResource.SetAnnotation(registryCacheItemFake);
							additionalResources.Add(resolvedResource);
							// UploadFile(settings, clientFhir, resolvedResource);
							unresolvableCanonicals.RemoveAll(uc => uc.canonical == dc.canonical);
							indirectCanonicals.Add(dc);
						}
						if (!r.Entry.Any())
						{
							// Console.WriteLine($"{dc.resourceType} Canonical {dc.canonical} was not present on the registry");
							unresolvableCanonicals.Add(dc);
						}
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"Error searching for {dc.resourceType} {dc.canonical} at {settings.ExternalRegistry} {ex.Message}");
					}
				}
				// now perform another scan for their dependencies too
				var initialCanonicals = additionalResources.Select(ec => new CanonicalDetails()
				{
					resourceType = ec.TypeName,
					canonical = (ec as IVersionableConformanceResource).Url,
					version = (ec as IVersionableConformanceResource).Version
				}).ToList();
				var dependentCanonicals = DependencyChecker.ScanForCanonicals(initialCanonicals.Union(externalNonCoreDirectCanonicals), additionalResources);
				foreach (var dc in dependentCanonicals)
				{
					try
					{
						if (settings.Verbose)
							Console.WriteLine($"Searching registry for {dc.resourceType} {dc.canonical}");
						var r = clientRegistry.Search(dc.resourceType, new[] { $"url={dc.canonical}" }, null, null, Hl7.Fhir.Rest.SummaryType.Data);
						if (r.Entry.Count > 1)
						{
							// Check if these are just more versions of the same thing, then to the canonical versioning thingy
							// to select the latest version.
							var cv = Hl7.Fhir.WebApi.CurrentCanonical.Current(r.Entry.Select(e => e.Resource as IVersionableConformanceResource));
							// remove all the others that aren't current.
							r.Entry.RemoveAll(e => e.Resource != cv as Resource);
						}
						if (r.Entry.Count() == 1)
						{
							var resolvedResource = r.Entry.First().Resource;
							// strip the SUBSETTED tag if it is there as we intentionally asked for data only (no narrative)
							resolvedResource.Meta?.Tag?.RemoveAll(t => t.Code == "SUBSETTED");
							resolvedResource.SetAnnotation(registryCacheItemFake);
							additionalResources.Insert(0, resolvedResource); // put dependencies at the start of the list
																			 // UploadFile(settings, clientFhir, resolvedResource);
							unresolvableCanonicals.RemoveAll(uc => uc.canonical == dc.canonical);
							indirectCanonicals.Add(dc);
						}
						if (!r.Entry.Any())
						{
							// Console.WriteLine($"{dc.resourceType} Canonical {dc.canonical} was not present on the registry");
							unresolvableCanonicals.Add(dc);
						}
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"Error searching for {dc.resourceType} {dc.canonical} at {settings.ExternalRegistry} {ex.Message}");
					}
				}

				// output a bundle with these additional resources
				if (!string.IsNullOrEmpty(settings.ExternalRegistryExportFile))
				{
					var bundle = new Bundle();
					bundle.Type = Bundle.BundleType.Collection;
					bundle.Entry.AddRange(additionalResources.Select(r => new Bundle.EntryComponent() { Resource = r }));
					var json = versionAgnosticProcessor.SerializeJson(bundle);
					File.WriteAllText(settings.ExternalRegistryExportFile, json);
				}
				dependencyResourcesToLoad.AddRange(additionalResources);
			}

			// If loading into a server, report any unresolvable canonicals
			if (!settings.TestPackageOnly)
			{
				Console.WriteLine();
				if (settings.Verbose)
				{
					ReportDependentCanonicalResourcesToConsole(settings, externalNonCoreDirectCanonicals);
					Console.WriteLine();
					ReportIndirectlyRequiredCanonicalResourcesToConsole(indirectCanonicals);
					Console.WriteLine();
				}
				ReportUnresolvedCanonicalResourcesToConsole(unresolvableCanonicals);
			}

			// Validate/upload the dependent resources
			if (settings.IncludeReferencedDependencies || settings.ValidateReferencedDependencies)
			{
				Console.WriteLine();
				Console.WriteLine("--------------------------------------");
				Console.WriteLine("Validate/upload dependencies:");
				foreach (var resource in dependencyResourcesToLoad)
				{
					var exampleName = resource.Annotation<ExampleName>()?.value ?? $"Registry {resource.TypeName}/{resource.Id}";
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

						if (settings.ValidateReferencedDependencies && !expressionValidator.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles))
							continue;

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
			if (!string.IsNullOrEmpty(settings.ExternalTerminologyServer))
			{
				Console.WriteLine();
				Console.WriteLine("--------------------------------------");
				Console.WriteLine($"Validate ValueSet complexity (and pre-expand if possible using {settings.ExternalTerminologyServer}):");
				var valueSets = resourcesToProcess.OfType<ValueSet>();
				var codeSystems = resourcesToProcess.OfType<CodeSystem>();
				//foreach(ValueSet vs in valueSets)
				//{
				//	if (expressionValidator.InMemoryResolver.ResolveByCanonicalUri(vs.Url) == null)
				//	{
				//		expressionValidator.InMemoryResolver.Add(vs, new PackageCacheItem());
				//	}
				//}
				

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
									Console.WriteLine($"ValueSet {vs.Url} failed to expand");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Contains.Count() > (settings.MaxExpansionSize ?? 1000))
								{
									Console.WriteLine($"ValueSet {vs.Url} expansion is too large to include ({expandedValueSet.Expansion.Contains.Count()} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Total.HasValue && expandedValueSet.Expansion.Contains.Count() != expandedValueSet.Expansion.Total.Value)
								{
									Console.WriteLine($"ValueSet {vs.Url} expansion is incomplete ({expandedValueSet.Expansion.Contains.Count()} of {expandedValueSet.Expansion.Total} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								if (expandedValueSet.Expansion.Total.HasValue && expandedValueSet.Expansion.Total.Value > (settings.MaxExpansionSize ?? 1000))
								{
									Console.WriteLine($"ValueSet {vs.Url} expansion is too large to include ({expandedValueSet.Expansion.Total} concepts)");
									failures.Add(vs.Url);
									continue;
								}
								// flag for limited expansion too?

								if (expandedValueSet.Expansion.NextElement != null)
								{
									Console.WriteLine($"ValueSet {vs.Url} expansion is too large to include");
									failures.Add(vs.Url);
									continue;
								}
								// Yay! we have an expansion we can use, so set it
								Console.WriteLine($"ValueSet {vs.Url} expansion included ({expandedValueSet.Expansion.Contains.Count()} concepts)");
								vs.Expansion = expandedValueSet.Expansion;
							}
							catch (Hl7.Fhir.Rest.FhirOperationException exExpand)
							{
								Console.WriteLine($"ValueSet {vs.Url} failed to expand on {settings.ExternalTerminologyServer}");
								Console.WriteLine($"  * pre-expansion required due to: {ex.Message}");

								if (exExpand.Outcome != null)
								{
									foreach (var issue in exExpand.Outcome.Issue)
									{
										if (issue.Severity != OperationOutcome.IssueSeverity.Information)
											Console.WriteLine($"  * {issue.Severity} {issue.Code} {issue.Details?.Text ?? issue.Details?.Coding.FirstOrDefault()?.Display}");
									}
								}
								else
								{
									Console.WriteLine($"  * {exExpand.Message}");
								}
							}
							catch (Exception exExpand)
							{
								Console.WriteLine($"ValueSet {vs.Url} failed to expand on {settings.ExternalTerminologyServer}: {exExpand.Message}");
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

			// Validate/upload the resources
			Console.WriteLine();
			Console.WriteLine("--------------------------------------");
			Console.WriteLine("Validate/upload package content:");
			foreach (var resource in resourcesToProcess)
			{
				var exampleName = resource.Annotation<ExampleName>()?.value ?? $"Registry {resource.TypeName}/{resource.Id}";
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


			// Ensure that all direct and indirect canonical resources (excluding core spec/extensions) are installed in the server
			DependencyChecker.VerifyDependenciesOnServer(settings, clientFhir, externalNonCoreDirectCanonicals);

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
				Console.WriteLine($"Package Canonical content summary: {resourcesToProcess.Count}");
				Console.WriteLine("\tCanonical Url\tCanonical Version\tStatus\tName");
				foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>().OrderBy(f => $"{f.Url}|{f.Version}"))
				{
					Console.WriteLine($"\t{resource.Url}\t{resource.Version}\t{resource.Status}\t{resource.Name}");
				}

				// Dependant Canonical Resources
				Console.WriteLine();
				Console.WriteLine("--------------------------------------");
				ReportDependentCanonicalResourcesToConsole(settings, externalNonCoreDirectCanonicals);

				// Indirect Dependant Canonical Resources
				Console.WriteLine();
				Console.WriteLine("--------------------------------------");
				ReportIndirectlyRequiredCanonicalResourcesToConsole(indirectCanonicals);

				// Unresolvable Canonical Resources
				Console.WriteLine();
				Console.WriteLine("--------------------------------------");
				ReportUnresolvedCanonicalResourcesToConsole(unresolvableCanonicals);

				Console.WriteLine();
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
				Console.WriteLine();
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
					externalNonCoreDirectCanonicals.Select(rc => new DependentResource()
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

		private static void ReportDependentCanonicalResourcesToConsole(Settings settings, List<CanonicalDetails> externalNonCoreDirectCanonicals)
		{
			Console.WriteLine($"Requires the following non-core canonical resources: {externalNonCoreDirectCanonicals.Count}");
			Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tPackage Source");
			foreach (var details in externalNonCoreDirectCanonicals.OrderBy(f => $"{f.canonical}|{f.version}"))
			{
				Console.Write($"\t{details.resourceType}\t{details.canonical}\t{details.version}");
				if (details.resource?.HasAnnotation<PackageCacheItem>() == true)
				{
					var cacheDetails = details.resource.Annotation<PackageCacheItem>();
					Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
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
						if (dr.HasAnnotation<PackageCacheItem>() == true)
						{
							var cacheDetails = dr.Annotation<PackageCacheItem>();
							Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
						}
						Console.WriteLine();
					}
				}
			}
		}

		private static void ReportIndirectlyRequiredCanonicalResourcesToConsole(List<CanonicalDetails> indirectCanonicals)
		{
			Console.WriteLine($"Indirectly requires the following non-core canonical resources: {indirectCanonicals.Count}");
			Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tPackage Source");
			foreach (var details in indirectCanonicals.OrderBy(f => $"{f.canonical}|{f.version}"))
			{
				Console.Write($"\t{details.resourceType}\t{details.canonical}\t{details.version}");
				if (details.resource?.HasAnnotation<PackageCacheItem>() == true)
				{
					var cacheDetails = details.resource.Annotation<PackageCacheItem>();
					Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
				}
				Console.WriteLine();
				foreach (var dr in details.requiredBy)
				{
					if (dr is IVersionableConformanceResource cr)
						Console.Write($"\t\t\t\t\t^- {cr.Url}|{cr.Version}");
					else
						Console.Write($"\t\t\t\t\t^- {dr.TypeName}/{dr.Id}");
					if (dr.HasAnnotation<PackageCacheItem>() == true)
					{
						var cacheDetails = dr.Annotation<PackageCacheItem>();
						Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
					}
					else if (dr.HasAnnotation<ExampleName>())
					{
						var exampleName = dr.Annotation<ExampleName>();
						Console.Write($"\t{exampleName.value}");
					}
					Console.WriteLine();
				}
			}
		}

		private static void ReportUnresolvedCanonicalResourcesToConsole(List<CanonicalDetails> unresolvableCanonicals)
		{
			Console.WriteLine($"Unable to resolve these canonical resources: {unresolvableCanonicals.Count}");
			Console.WriteLine("\tResource Type\tCanonical Url\tVersion\tPackage Source");
			foreach (var details in unresolvableCanonicals.OrderBy(f => $"{f.canonical}|{f.version}"))
			{
				Console.WriteLine($"\t{details.resourceType}\t{details.canonical}\t{details.version}");
				foreach (var dr in details.requiredBy)
				{
					if (dr is IVersionableConformanceResource cr)
						Console.Write($"\t\t\t\t\t^- {cr.Url}|{cr.Version}");
					else
						Console.Write($"\t\t\t\t\t^- {dr.TypeName}/{dr.Id}");
					if (dr.HasAnnotation<PackageCacheItem>() == true)
					{
						var cacheDetails = dr.Annotation<PackageCacheItem>();
						Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
					}
					if (dr.HasAnnotation<ExampleName>())
					{
						var exampleName = dr.Annotation<ExampleName>();
						Console.Write($"\t{exampleName.value}");
					}
					Console.WriteLine();
				}
			}
		}

		private static List<Resource> ReadResourcesFromPackage(Settings settings, TarReader reader, Common_Processor versionAgnosticProcessor, List<string> errs, List<string> errFiles)
		{
			List<Resource> resourcesToProcess = new();
			TarEntry entry;
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
									resource = versionAgnosticProcessor.ParseXml(xr);
								}
							}
							else if (exampleName.EndsWith(".json"))
							{
								using (var jr = SerializationUtil.JsonReaderFromStream(stream))
								{
									resource = versionAgnosticProcessor.ParseJson(jr);
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
			return resourcesToProcess;
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
						Console.Write($"    {original.TypeName}/{original.Id} unchanged {(resource as IVersionableConformanceResource)?.Version}");
						if (resource.HasAnnotation<PackageCacheItem>() == true)
						{
							var cacheDetails = resource.Annotation<PackageCacheItem>();
							Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
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
							if (resource.HasAnnotation<PackageCacheItem>() == true)
							{
								var cacheDetails = resource.Annotation<PackageCacheItem>();
								Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
							}
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
			if (resource.HasAnnotation<PackageCacheItem>() == true)
			{
				var cacheDetails = resource.Annotation<PackageCacheItem>();
				Console.Write($"\t({cacheDetails.packageId}|{cacheDetails.packageVersion})");
			}
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