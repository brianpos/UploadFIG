extern alias r5;
using Hl7.Fhir.FhirPath.Validator;
using r5::Hl7.Fhir.Model;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Hl7.FhirPath.Sprache;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using UploadFIG.PackageHelpers;

namespace UploadFIG
{
    // Adds the version specific search parameter validation
    internal class ExpressionValidatorR5 : ExpressionValidator
    {
        public ExpressionValidatorR5(Common_Processor processor, bool validateQuestionnaire) : base(processor)
        {
            _validateQuestionnaire = validateQuestionnaire;
		}
		bool _validateQuestionnaire;

        List<SearchParameter> _searchParameters;
        public override void PreValidation(PackageDetails pd, DependencyChecker depChecker, bool verboseMode, List<String> errFiles)
        {
			base.PreValidation(pd, depChecker, verboseMode, errFiles);
			_searchParameters = depChecker.AllResources(pd).OfType<SearchParameter>().ToList();
			CommonZipSource zipSource = r5::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r5.zip"));
			_inMemoryResolver = new InMemoryResolver(pd, depChecker, _processor, errFiles, verboseMode);
			_source = new CachedResolver(
							new MultiResolver(
								zipSource,
								_inMemoryResolver
							)
						);
		}

		internal override bool Validate(string exampleName, Resource resource, ref long failures, ref long validationErrors, List<string> errFiles)
        {
			_inMemoryResolver.ProcessingResource(resource);
			
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
                var validator = new r5.Hl7.Fhir.StructuredDataCapture.QuestionnaireValidator();
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

		internal override void PatchKnownIssues(string packageId, string packageVersion, Resource resource)
		{
			if (resource is StructureDefinition sd)
			{
				if (sd.FhirVersion.HasValue && sd.FhirVersion != FHIRVersion.N5_0 && sd.FhirVersion != FHIRVersion.N5_0_0)
				{
					Console.WriteLine($"    #---> Error validating StructureDefinition/{sd.Id} ({sd.Url}): {sd.Title}");
					Console.WriteLine($"        Only FHIR version 5.0 is supported - removed inconsistent version {sd.FhirVersion.GetLiteral()}");
					sd.FhirVersion = null;
				}
			}

			if (packageId == "us.nlm.vsac" && resource is ValueSet vs)
			{
				if (vs.Meta?.Profile.Any(p => p == "http://hl7.org/fhir/StructureDefinition/shareablevalueset") == true)
					vs.Meta.Profile = vs.Meta.Profile.Where(p => p != "http://hl7.org/fhir/StructureDefinition/shareablevalueset");

				var author = vs.GetExtension("http://hl7.org/fhir/StructureDefinition/valueset-author");
				if (author != null)
				{
					// re-write the extension if the datatype is incorrect
					if (author.Value is FhirString fs)
						author.Value = new ContactDetail() { Name = fs.Value };
				}
				var effectiveDate = vs.GetExtension("http://hl7.org/fhir/StructureDefinition/valueset-effectiveDate");
				if (effectiveDate != null)
				{
					// re-write the extension if the datatype is incorrect
					if (effectiveDate.Value is Date dt)
						effectiveDate.Value = new FhirDateTime(dt.Value);
				}


				if (vs.Jurisdiction?.Any() == true)
				{
					// remove any empty jurisdictions (DAR) as these don't have a text or coding
					foreach (var jurisdiction in vs.Jurisdiction.ToArray())
					{
						var dar = jurisdiction.GetExtension("http://hl7.org/fhir/StructureDefinition/data-absent-reason");
						if (dar.Value is FhirString fs)
						{
							dar.Value = new Code(fs.Value);
						}
						if (dar.Value is Code code)
						{
							if (code.Value == "UNKNOWN")
								code.Value = "unknown";
							if (code.Value == "unknown")
								vs.Jurisdiction.Remove(jurisdiction);
						}
					}
				}
			}
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
				ConsoleEx.WriteLine(ConsoleColor.Red, $"    #---> Error validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			if (outcome.Warnings > 0)
			{
				ConsoleEx.WriteLine(ConsoleColor.Yellow, $"    #---> Warning validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			if (outcome.Issue.Count(i => i.Severity == OperationOutcome.IssueSeverity.Information) > 0)
			{
				ConsoleEx.WriteLine(ConsoleColor.Gray, $"    #---> Information validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
				ReportOutcomeMessages(outcome);
				Console.WriteLine();
				return false;
			}
			return true;
        }
    }
}
