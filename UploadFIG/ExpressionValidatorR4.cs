extern alias r4;
using Hl7.Fhir.FhirPath.Validator;
using r4::Hl7.Fhir.Model;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Hl7.FhirPath.Sprache;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;

namespace UploadFIG
{
	// Adds the version specific search parameter validation
	internal class ExpressionValidatorR4 : ExpressionValidator
	{
		public ExpressionValidatorR4(Common_Processor processor, bool validateQuestionnaire) : base(processor)
		{
			_validateQuestionnaire = validateQuestionnaire;
		}
		public bool _validateQuestionnaire;

		List<SearchParameter> _searchParameters;
		public override void PreValidation(Dictionary<string, string> dependencies, List<Resource> resources)
		{
			base.PreValidation(dependencies, resources);
			_searchParameters = resources.OfType<SearchParameter>().ToList();
			CommonZipSource zipSource = r4::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4.zip"));
			_source = new CachedResolver(
							new MultiResolver(
								zipSource,
								new InMemoryResolver(_profiles)
							)
						);
		}

		internal override bool Validate(string exampleName, Resource resource, ref long failures, ref long validationErrors, List<string> errFiles)
		{
			if (resource is SearchParameter sp)
			{
				if (!sp.Base.Any())
				{
					// Quietly skip them
					Console.Error.WriteLine($"ERROR: ({exampleName}) Search parameter with no base");
					System.Threading.Interlocked.Increment(ref failures);
					// DebugDumpOutputXml(resource);
					errFiles.Add(exampleName);
					return false;
				}
				if (!ValidateSearchExpression(sp))
					validationErrors++;
			}
			if (resource is Questionnaire q && _validateQuestionnaire)
			{
				var validator = new r4.Hl7.Fhir.StructuredDataCapture.QuestionnaireValidator();
				var outcome = validator.Validate(q).WaitResult();
				if (!outcome.Success)
				{
					Console.WriteLine($"    #---> Error validating Questionnaire/{q.Id} ({q.Url}): {q.Title}");
					ReportOutcomeMessages(outcome);
					Console.WriteLine();
					validationErrors++;
					return false;
				}
			}

			return base.Validate(exampleName, resource, ref failures, ref validationErrors, errFiles);
		}

		VersionAgnosticSearchParameter ToVaSpd(ModelInfo.SearchParamDefinition spd)
		{
			return new VersionAgnosticSearchParameter()
			{
				Resource = spd.Resource,
				Code = spd.Code,
				Type = spd.Type,
				Expression = spd.Expression,
				Url = spd.Url,
				Name = spd.Name,
				Target = spd.Target?.Select(t => t.GetLiteral()).ToArray(),
				Component = spd.Component?.Select(c => new SearchParamComponent()
				{
					Definition = c.Definition,
					Expression = c.Expression,
				}).ToArray(),
			};
		}

		IEnumerable<VersionAgnosticSearchParameter> ToVaSpd(SearchParameter sp)
		{
			List<VersionAgnosticSearchParameter> result = new();
			foreach (var resource in sp.Base)
			{
				result.Add(new VersionAgnosticSearchParameter()
				{
					Resource = resource.GetLiteral(),
					Type = sp.Type.Value,
					Code = sp.Code,
					Expression = sp.Expression,
					Url = sp.Url,
					Name = sp.Name,
					Target = sp.Target?.Select(t => t.GetLiteral()).ToArray(),
					Component = sp.Component?.Select(c => new SearchParamComponent()
					{
						Definition = c.Definition,
						Expression = c.Expression,
					}).ToArray(),
				});
			}
			return result;
		}

		public bool ValidateSearchExpression(SearchParameter sp)
		{
			var outcome = new OperationOutcome();
			var vaSps = ToVaSpd(sp);
			foreach (var vaSp in vaSps)
			{
				if (string.IsNullOrEmpty(vaSp.Code))
				{
					LogError(outcome.Issue, OperationOutcome.IssueType.Required, SearchCodeMissing, $"Search parameter {sp.Url} does not define the 'code' property which defines the value to use on the request URL");
				}
				if (vaSp.Type == SearchParamType.Special)
                {
					// Special search parameters don't have expressions
					LogWarning(outcome.Issue, OperationOutcome.IssueType.Informational, SpecialSearchParameter, $"Search parameter {sp.Url} of type 'special' requires custom implementation to work")
						.Severity = OperationOutcome.IssueSeverity.Information;
				}
				else if (string.IsNullOrEmpty(vaSp.Expression) && vaSp.Type != SearchParamType.Special)
					LogError(outcome.Issue, OperationOutcome.IssueType.Required, SearchExpressionMissing, $"Search parameter does not contain a fhirpath expression to define its behaviour");
				else
				{
					try
					{
						SearchExpressionValidator v = new SearchExpressionValidator(_processor.ModelInspector,
							  _processor.SupportedResources,
							  _processor.OpenTypes,
							  (url) =>
							  {
								  return _searchParameters.Where(sp => sp.Url == url)
										  .SelectMany(v => ToVaSpd(v))
										  .FirstOrDefault()
									  ?? ModelInfo.SearchParameters.Where(sp => sp.Url == url)
										  .Select(v => ToVaSpd(v))
										  .FirstOrDefault();
							  });
						v.IncludeParseTreeDiagnostics = true;
						v.CreateFhirPathValidator = CreateFhirPathValidator;
						var issues = v.Validate(vaSp.Resource, vaSp.Code, vaSp.Expression, vaSp.Type, vaSp.Url, vaSp);
						outcome.Issue.AddRange(issues);
					}
					catch (Exception ex)
					{
						outcome.Issue.Add(new OperationOutcome.IssueComponent()
						{
							Severity = OperationOutcome.IssueSeverity.Error,
							Code = OperationOutcome.IssueType.Exception,
							Details = new CodeableConcept(InternalProcessingException.System, InternalProcessingException.Code, InternalProcessingException.Display, null),
							Diagnostics = ex.Message,
						});
					}
				}
			}

			if (outcome.Errors > 0 || outcome.Fatals > 0)
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"    #---> Error validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				Console.ForegroundColor = ocolor;

				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			if (outcome.Warnings > 0)
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"    #---> Warning validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				Console.ForegroundColor = ocolor;

				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			if (outcome.Issue.Count(i => i.Severity == OperationOutcome.IssueSeverity.Information) > 0)
			{
				var ocolor = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Gray;
				Console.WriteLine($"    #---> Information validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				Console.ForegroundColor = ocolor;

				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			return true;
		}
	}
}
