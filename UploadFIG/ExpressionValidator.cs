extern alias r4;

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
		public IResourceResolver Source { get { return _source; } }
		DependencyChecker _depChecker;

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

		public virtual void PreValidation(PackageDetails pd, DependencyChecker depChecker, bool verboseMode, List<String> errFiles)
		{
			_depChecker = depChecker;
		}

		internal virtual bool Validate(string exampleName, Resource resource, ref long failures, ref long validationErrors, List<string> errFiles)
		{
			if (resource is StructureDefinition sd)
			{
				if (!ValidateInvariants(sd))
					validationErrors++;

				// Check if there are any types in the differentials that aren't valid for this version of FHIR
				if (sd.Differential?.Element?.Any() == true)
				{
					foreach (var element in sd.Differential.Element)
					{
						foreach (var t in element.Type)
						{
							CheckElementType(exampleName, ref validationErrors, element, t, "Differential");
						}
					}
				}
				if (sd.Snapshot?.Element?.Any() == true)
				{
					foreach (var element in sd.Snapshot.Element)
					{
						foreach (var t in element.Type)
						{
							CheckElementType(exampleName, ref validationErrors, element, t, "Snapshot");
						}
					}
				}
			}
			return true;
		}

		internal virtual void PatchKnownIssues(string packageId, string packageVersion, Resource resource)
		{

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
				ConsoleEx.WriteLine(ConsoleColor.Red, $"    #---> Error validating invariant {canonicalUrl}: {key}");
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
				ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    #---> Warning validating invariant {canonicalUrl}: {key}");
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
				ConsoleEx.WriteLine(ConsoleColor.Gray, $"    #---> Information validating invariant {canonicalUrl}: {key}");
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

		protected void CheckElementType(string exampleName, ref long validationErrors, ElementDefinition element, ElementDefinition.TypeRefComponent t, string sourceElement)
		{
			if (!IsValidType(t.Code))
			{
				if (!(element.Path.EndsWith(".resource") && _processor.ModelInspector.IsKnownResource(t.Code)))
				{
					ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: ({exampleName}) {sourceElement} Element {element.ElementId} has type `{t.Code}` which is not valid in FHIR v{_processor.ModelInspector.FhirVersion}");
					validationErrors++;
				}
			}

			// Check the target profile(s)
			if (t.TargetProfile?.Any() == true)
			{
				// Non versioned should expand?
				foreach (var tp in t.TargetProfile)
				{
					// skip R5 pre-adoption extensions (till that is a resolvable thing)
					if (!tp.StartsWith("http://hl7.org/fhir/5.0/StructureDefinition/") && _source.FindStructureDefinition(tp) == null)
					{
						ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: ({exampleName}) {sourceElement} Element {element.ElementId} has target profile `{tp}` which does not resolve in FHIR v{_processor.ModelInspector.FhirVersion}");
						validationErrors++;
					}
				}
			}

			// Check type profiles
			if (t.Profile?.Any() == true)
			{
				foreach (var tp in t.Profile)
				{
					// skip R5 pre-adoption extensions (till that is a resolvable thing)
					if (!tp.StartsWith("http://hl7.org/fhir/5.0/StructureDefinition/") && _source.FindStructureDefinition(tp) == null)
					{
						ConsoleEx.WriteLine(ConsoleColor.Red, $"ERROR: ({exampleName}) {sourceElement} Element {element.ElementId} has type profile `{tp}` which does not resolve in FHIR v{_processor.ModelInspector.FhirVersion}");
						validationErrors++;
					}
				}
			}

		}

		protected bool IsValidType(string code)
		{
			if (code == null)
				return true; // don't warn on missing types.

			if (!this._processor.ModelInspector.IsDataType(code)
				&& !this._processor.ModelInspector.IsPrimitive(code)
				&& code != "Resource"
				&& !code.StartsWith("http://hl7.org/fhirpath/System.")
				)
				return false;
			return true;
		}
	}

	internal class InMemoryResolver : IResourceResolver
	{
		internal InMemoryResolver(PackageDetails pd, DependencyChecker depChecker, Common_Processor versionAgnosticProcessor, List<String> errFiles)
		{
			_pd = pd;
			_depChecker = depChecker;
			_processor = versionAgnosticProcessor;
			_errFiles = errFiles;
		}
		PackageDetails _pd;
		DependencyChecker _depChecker;
		Resource _resource;
		Common_Processor _processor;
		List<String> _errFiles;

		public void ProcessingResource(Resource resource)
		{
			_resource = resource;
		}

		public Resource ResolveByCanonicalUri(string uri)
		{
			Canonical c = new Canonical(uri);
			var cd = new CanonicalDetails()
			{
				canonical = c.Uri,
				version = c.Version,
				resourceType = "StructureDefinition",
			};
			cd.requiredBy.Add(_resource);

			var matches = _depChecker.ResolveCanonical(_pd, cd, _processor, _errFiles);
			var useResource = CurrentCanonicalFromPackages.Current(matches);
			if (useResource != null)
			{
				var distinctVersionSources = matches.Select(m => ResourcePackageSource.PackageSourceVersion(m)).Distinct();
				if (distinctVersionSources.Count() > 1)
				{
					Console.Write($"    Resolved {cd.canonical}|{cd.version} with ");
					ConsoleEx.Write(ConsoleColor.Yellow, ResourcePackageSource.PackageSourceVersion(useResource));
					Console.WriteLine($" from {String.Join(", ", distinctVersionSources)}");
				}
				cd.resource = useResource as Resource;
			}
			return useResource as Resource;
		}

		public Resource ResolveByUri(string uri)
		{
			throw new NotImplementedException();
		}
	}

}
