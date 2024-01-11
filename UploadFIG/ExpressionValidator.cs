using Firely.Fhir.Packages;
using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath.Sprache;
using UploadFIG.PackageHelpers;

namespace UploadFIG
{
	/// <summary>
	/// Version agnostic expression validations
	/// </summary>
	internal abstract class ExpressionValidator : ExpressionValidatorBase
	{
		protected Common_Processor _processor;
		FhirPathCompiler _compiler;
		protected IResourceResolver _source;
		protected InMemoryResolver _inMemoryResolver;
		public InMemoryResolver InMemoryResolver { get { return _inMemoryResolver; } }

		public ExpressionValidator(Common_Processor processor)
		{
			_processor = processor;

			// include all the conformance types
			_processor.ModelInspector.Import(typeof(StructureDefinition).Assembly);

			Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
			SymbolTable symbolTable = new(FhirPathCompiler.DefaultSymbolTable);
			_compiler = new FhirPathCompiler(symbolTable);
		}

		protected BaseFhirPathExpressionVisitor CreateFhirPathValidator()
		{
			return new ExtensionResolvingFhirPathExpressionVisitor(
				_source,
				_processor.ModelInspector,
				_processor.SupportedResources,
				_processor.OpenTypes);
		}

		protected List<StructureDefinition> _profiles;
		protected List<PackageCacheItem> _dependencyProfiles = new List<PackageCacheItem>();

		public List<PackageCacheItem> DependencyProfiles { get { return _dependencyProfiles; } }

		public virtual void PreValidation(Dictionary<string, string> dependencies, List<Resource> resources, bool verboseMode)
		{
			_profiles = resources.OfType<StructureDefinition>().ToList();
			RecursivelyScanPackageExtensions(dependencies, verboseMode);
		}

		public void RecursivelyScanPackageExtensions(Dictionary<string, string> dependencies, bool verboseMode)
		{
			// Prepare our own cache of fhir packages in this projects AppData folder
			var cache = new TempPackageCache();
			Queue<KeyValuePair<string, string>> depPackages = new();
			var scannedPackages = new List<string>();
			foreach (var dp in dependencies)
			{
				var key = $"{dp.Key}|{dp.Value}";
				if (!scannedPackages.Contains(key))
				{
					depPackages.Enqueue(dp);
					if (verboseMode)
						Console.WriteLine($"Added {dp.Key}|{dp.Value} for processing");
					scannedPackages.Add(key);
				}
			}
			while (depPackages.Count > 0)
			{
				var dp = depPackages.Dequeue();

				if (dp.Value == "current")
				{
					var oldColor = Console.ForegroundColor; 
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine($"      Unable to scan 'current' dependency {dp.Key}");
					Console.ForegroundColor = oldColor;
					continue;
				}

				Stream packageStream = cache.GetPackageStream(dp.Key, dp.Value);

				if (packageStream == null)
				{
					// No package, so just need to continue
					continue;
				}

				PackageManifest manifest;
				using (packageStream)
				{
					manifest = TempPackageCache.ReadManifest(packageStream);
					if (manifest == null)
						continue; // can't process the package without a manifest

					// Skip core packages that are handled elsewhere
					if (manifest.Canonical == "http://hl7.org/fhir")
						continue;
					//if (manifest.Canonical == "http://hl7.org/fhir/extensions")
					//	continue;
					//if (manifest.Canonical == "http://terminology.hl7.org")
					//	continue;

					PackageIndex index = TempPackageCache.ReadPackageIndex(packageStream);

					// Scan this package to see if any content is in the index
					if (index != null)
					{
						// System.Diagnostics.Trace.WriteLine($"Scanning index in {manifest.Name}");
						var files = index.Files.Where(f => !string.IsNullOrEmpty(f.url));
						if (files.Any())
						{
							// Read these files from the package
							foreach (var file in files)
							{
								var cacheItem = new PackageCacheItem()
								{
									packageId = manifest.Name,
									packageVersion = manifest.Version,
									filename = file.filename,
									resourceType = file.resourceType,
									id = file.id,
									url = file.url,
									version = file.version,
									type = file.type
								};
								_dependencyProfiles.Add(cacheItem);
							}
						}
					}
				}

				// Scan through this packages dependencies and see if I need to add more to the queue for processing
				if (manifest.Dependencies != null)
				{
					foreach (var dep in manifest.Dependencies)
					{
						var key = $"{dep.Key}|{dep.Value}";
						if (!scannedPackages.Contains(key))
						{
							scannedPackages.Add(key);
							if (!dependencies.ContainsKey(dep.Key))
							{
								depPackages.Enqueue(dep);
								if (verboseMode)
									Console.WriteLine($"Added {dep.Key}|{dep.Value} for processing (dependency of {manifest.Name}|{manifest.Version})");
							}
							else
							{
								if (dep.Value != dependencies[dep.Key])
									Console.WriteLine($"      {manifest.Name}|{manifest.Version} => {dep.Key}|{dep.Value} is already included with version {dependencies[dep.Key]}");
							}
						}
					}
				}
			}
		}

		internal virtual bool Validate(string exampleName, Resource resource, ref long failures, ref long validationErrors, List<string> errFiles)
		{
			if (resource is StructureDefinition sd)
			{
				if (!ValidateInvariants(sd))
					validationErrors++;
			}
			return true;
		}

		public bool ValidateInvariants(StructureDefinition sd)
		{
			bool result = true;
			if (sd != null && sd.Kind == StructureDefinition.StructureDefinitionKind.Resource && sd.Abstract == false)
			{
				var elements = sd.Differential.Element.Where(e => e.Constraint.Any()).ToList();
				if (elements.Any())
				{
					foreach (var ed in elements)
					{
						foreach (var c in ed.Constraint)
						{
							if (!string.IsNullOrEmpty(c.Expression))
							{
								result = result && VerifyInvariant(sd.Url, ed.Path, c.Key, c.Expression);
							}
						}
					}
				}
			}
			return result;
		}

		private bool VerifyInvariant(string canonicalUrl, string path, string key, string expression)
		{
			var visitor = CreateFhirPathValidator();
			visitor.SetContext(path);
			var pe = _compiler.Parse(expression);
			var r = pe.Accept(visitor);

			if (!visitor.Outcome.Success
				|| "boolean" != r.ToString())
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"    #---> Error validating invariant {canonicalUrl}: {key}");
				Console.ForegroundColor = ocolor;
				// Console.WriteLine(visitor.ToString());
				// AssertIsTrue(visitor.Outcome.Success, "Expected Invariant to pass");
				AssertIsTrue(false, $"Context: {path}");
				AssertIsTrue(false, $"Expression: {expression}");
				AssertIsTrue(false, $"Return type: {r}");
				ReportOutcomeMessages(visitor.Outcome);
				// Console.WriteLine(visitor.Outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
				AssertIsTrue("boolean" == r.ToString(), "Invariants must return a boolean");
				Console.WriteLine();
				return false;
			}
			else if (visitor.Outcome.Warnings > 0)
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"    #---> Warning validating invariant {canonicalUrl}: {key}");
				Console.ForegroundColor = ocolor;
				// Console.WriteLine(visitor.ToString());
				// AssertIsTrue(visitor.Outcome.Success, "Expected Invariant to pass");
				AssertIsTrue(false, $"Context: {path}");
				AssertIsTrue(false, $"Expression: {expression}");
				AssertIsTrue(false, $"Return type: {r}");
				ReportOutcomeMessages(visitor.Outcome);
				// Console.WriteLine(visitor.Outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
				AssertIsTrue("boolean" == r.ToString(), "Invariants must return a boolean");
				Console.WriteLine();
				return false;
			}
			else if (visitor.Outcome.Issue.Count(i => i.Severity == OperationOutcome.IssueSeverity.Information) > 0)
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine($"    #---> Information validating invariant {canonicalUrl}: {key}");
				Console.ForegroundColor = ocolor;
				// Console.WriteLine(visitor.ToString());
				// AssertIsTrue(visitor.Outcome.Success, "Expected Invariant to pass");
				AssertIsTrue(false, $"Context: {path}");
				AssertIsTrue(false, $"Expression: {expression}");
				AssertIsTrue(false, $"Return type: {r}");
				ReportOutcomeMessages(visitor.Outcome);
				// Console.WriteLine(visitor.Outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
				AssertIsTrue("boolean" == r.ToString(), "Invariants must return a boolean");
				Console.WriteLine();
				return false;
			}
			return true;
		}
	}

	internal class InMemoryResolver : IResourceResolver
	{
		internal InMemoryResolver(Common_Processor processor, List<StructureDefinition> sds, List<PackageCacheItem> dependencyProfiles)
		{
			_processor = processor;
			_sds = sds;
			_dependencyProfiles = new Dictionary<string, PackageCacheItem>();

			foreach (var dep in dependencyProfiles)
			{
				if (!_dependencyProfiles.ContainsKey(dep.url))
					_dependencyProfiles.Add(dep.url, dep);
				else
				{
					var depUsing = _dependencyProfiles[dep.url];
					depUsing.duplicates.Add(dep);
					// Console.WriteLine($"Detected multiple instances of {dep.url} in {dep.packageId}|{dep.packageVersion} {dep.filename} {dependencyProfiles.IndexOf(dep)}");
					// Console.WriteLine($"      using: {depUsing.packageId}|{depUsing.packageVersion} {depUsing.filename} {dependencyProfiles.IndexOf(depUsing)}");
				}
			}
		}
		TempPackageCache _cache = new TempPackageCache();
		protected Common_Processor _processor;
		List<StructureDefinition> _sds;
		Dictionary<string, PackageCacheItem> _dependencyProfiles;

		public Resource ResolveByCanonicalUri(string uri)
		{
			if (_dependencyProfiles.ContainsKey(uri))
			{
				var cacheItem = _dependencyProfiles[uri];

				if (cacheItem.duplicates.Any())
				{
					Console.WriteLine($"Detected multiple instances of {uri} using {cacheItem.packageId}|{cacheItem.packageVersion} {cacheItem.filename}   alternates ignored: {string.Join(", ", cacheItem.duplicates.Select(d => $"{d.filename} in {d.packageId}|{d.packageVersion}"))}");
					// Console.WriteLine($"      using: {depUsing.packageId}|{depUsing.packageVersion} {depUsing.filename} {dependencyProfiles.IndexOf(depUsing)}");
				}

				Stream packageStream = _cache.GetPackageStream(cacheItem.packageId, cacheItem.packageVersion);
				var content = TempPackageCache.ReadResourceContent(packageStream, cacheItem.filename);
				if (content != null)
				{
					Resource resource;
					if (cacheItem.filename.EndsWith(".json"))
					{
						resource = _processor.ParseJson(content);
					}
					else
					{
						resource = _processor.ParseXml(content);
					}
					resource.SetAnnotation(new ExampleName() { value = cacheItem.filename });
					resource.SetAnnotation(cacheItem);
					return resource;
				}
			}

			return _sds.FirstOrDefault(sd => sd.Url == uri);
		}

		public Resource ResolveByUri(string uri)
		{
			if (_dependencyProfiles.ContainsKey(uri))
			{
				var cacheItem = _dependencyProfiles[uri];
				Stream packageStream = _cache.GetPackageStream(cacheItem.packageId, cacheItem.packageVersion);
				var content = TempPackageCache.ReadResourceContent(packageStream, cacheItem.filename);
				if (content != null)
				{
					Resource resource;
					if (cacheItem.filename.EndsWith(".json"))
					{
						resource = _processor.ParseJson(content);
					}
					else
					{
						resource = _processor.ParseXml(content);
					}
					resource.SetAnnotation(new ExampleName() { value = cacheItem.filename });
					resource.SetAnnotation(cacheItem);
					return resource;
				}
			}

			return _sds.FirstOrDefault(sd => sd.Url == uri);
		}
	}

}
