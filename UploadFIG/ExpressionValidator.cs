using Firely.Fhir.Packages;
using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Support;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath.Sprache;

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

		public virtual void PreValidation(Dictionary<string, string> dependencies, List<Resource> resources)
		{
			_profiles = resources.OfType<StructureDefinition>().ToList();
			RecursivelyScanPackageExtensions(dependencies);
		}

		public void RecursivelyScanPackageExtensions(Dictionary<string, string> dependencies)
		{
			// Prepare our own cache of fhir packages in this projects AppData folder
			var cache = new TempPackageCache();
			Queue<KeyValuePair<string, string>> depPackages = new();
			foreach (var dp in dependencies)
			{
				depPackages.Enqueue(dp);
				// Console.WriteLine($"Added {dp.Key}|{dp.Value} for processing");
			}
			while (depPackages.Count > 0)
			{
				var dp = depPackages.Dequeue();

				if (dp.Value == "current")
				{
					Console.WriteLine($"      Unable to scan 'current' dependency {dp.Key}");
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
					if (manifest.Canonical == "http://hl7.org/fhir/extensions")
						continue;
					if (manifest.Canonical == "http://terminology.hl7.org")
						continue;

					PackageIndex index = TempPackageCache.ReadPackageIndex(packageStream);

					// Scan this package to see if any content is in the index
					if (index != null)
					{
						System.Diagnostics.Trace.WriteLine($"Scanning index in {manifest.Name}");
						var files = index.Files.Where(f => f.resourceType == "StructureDefinition");
						if (files.Any())
						{
							// Read these files from the package
							foreach (var file in files)
							{
								var content = TempPackageCache.ReadResourceContent(packageStream, file.filename);
								if (content != null)
								{
									Resource resource;
									if (files.First().filename.EndsWith(".json"))
									{
										resource = _processor.ParseJson(content);
									}
									else
									{
										resource = _processor.ParseXml(content);
									}
									if (resource is StructureDefinition sd)
									{
										// Add an annotation to indicate where it came from?
										_profiles.Add(sd);
										// Console.WriteLine($"	    {manifest.Canonical} {sd.Url}|{sd.Version} included");
									}
								}
							}
						}
					}
				}

				// Scan through this packages dependencies and see if I need to add more to the queue for processing
				if (manifest.Dependencies != null)
				{
					foreach (var dep in manifest.Dependencies)
					{
						if (!dependencies.ContainsKey(dep.Key))
						{
							depPackages.Enqueue(dep);
							// Console.WriteLine($"Added {dep.Key}|{dep.Value} for processing");
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
		internal InMemoryResolver(List<StructureDefinition> sds)
		{
			_sds = sds;
		}
		List<StructureDefinition> _sds;
		public Resource ResolveByCanonicalUri(string uri)
		{
			return _sds.FirstOrDefault(sd => sd.Url == uri);
		}

		public Resource ResolveByUri(string uri)
		{
			return _sds.FirstOrDefault(sd => sd.Url == uri);
		}
	}

}
