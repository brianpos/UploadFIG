using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Support;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Hl7.FhirPath.Sprache;

namespace UploadFIG
{
    internal class ExpressionValidator
    {
        private readonly ModelInspector _mi = ModelInspector.ForAssembly(typeof(Patient).Assembly);
        FhirPathCompiler _compiler;

        public ExpressionValidator()
        {
            // include all the conformance types
            _mi.Import(typeof(StructureDefinition).Assembly);

            Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
            SymbolTable symbolTable = new(FhirPathCompiler.DefaultSymbolTable);
            _compiler = new FhirPathCompiler(symbolTable);
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
            if (expression.Contains("descendants()"))
            {
                Console.WriteLine($"Warning: Fhirpath invariant testing does not support the descendants() function skipping this expression");
                return false;
            }

            var visitor = new FhirPathExpressionVisitor();
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
            return true;
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

        const string ErrorNamespace = "http://fhirpath-lab.com/CodeSystem/search-exp-errors";
        readonly static Coding SearchCodeMissing = new(ErrorNamespace, "SE0101", "No 'code' property in search parameter");
        readonly static Coding SearchExpressionMissing = new(ErrorNamespace, "SE0101", "No 'expression' property in search parameter");

        private void LogError(List<OperationOutcome.IssueComponent> results, OperationOutcome.IssueType issueType, Coding detail, string message, string diagnostics = null)
        {
            // Console.WriteLine(message);
            var issue = new Hl7.Fhir.Model.OperationOutcome.IssueComponent()
            {
                Severity = Hl7.Fhir.Model.OperationOutcome.IssueSeverity.Error,
                Code = issueType,
                Details = new Hl7.Fhir.Model.CodeableConcept(detail.System, detail.Code, detail.Display, message)
            };
            if (!string.IsNullOrEmpty(diagnostics))
                issue.Diagnostics = diagnostics;
            results.Add(issue);
        }
        private void LogWarning(List<OperationOutcome.IssueComponent> results, OperationOutcome.IssueType issueType, Coding detail, string message, string diagnostics = null)
        {
            // Console.WriteLine(message);
            var issue = new Hl7.Fhir.Model.OperationOutcome.IssueComponent()
            {
                Severity = Hl7.Fhir.Model.OperationOutcome.IssueSeverity.Warning,
                Code = issueType,
                Details = new Hl7.Fhir.Model.CodeableConcept(detail.System, detail.Code, detail.Display, message)
            };
            if (!string.IsNullOrEmpty(diagnostics))
                issue.Diagnostics = diagnostics;
            results.Add(issue);
        }

        public bool ValidateSearchExpression(SearchParameter sp, List<SearchParameter> localSearchParameters)
        {
            var outcome = new OperationOutcome();
            var vaSps = ToVaSpd(sp);
            foreach (var vaSp in vaSps)
            {
                if (string.IsNullOrEmpty(vaSp.Code))
                {
                    LogError(outcome.Issue, OperationOutcome.IssueType.Required, SearchCodeMissing, $"Search parameter {sp.Url} does not define the 'code' property which defines the value to use on the request URL");
                }
                if (string.IsNullOrEmpty(vaSp.Expression) && vaSp.Type != SearchParamType.Special)
                    LogError(outcome.Issue, OperationOutcome.IssueType.Required, SearchExpressionMissing, $"Search parameter does not contain a fhirpath expression to define its behaviour");
                else
                {
                    SearchExpressionValidator v = new SearchExpressionValidator(_mi,
                          Hl7.Fhir.Model.ModelInfo.SupportedResources,
                          Hl7.Fhir.Model.ModelInfo.OpenTypes,
                          (url) =>
                          {
                              return localSearchParameters.Where(sp => sp.Url == url)
                                      .SelectMany(v => ToVaSpd(v))
                                      .FirstOrDefault()
                                  ?? ModelInfo.SearchParameters.Where(sp => sp.Url == url)
                                      .Select(v => ToVaSpd(v))
                                      .FirstOrDefault();
                          });
                    v.IncludeParseTreeDiagnostics = true;
                    var issues = v.Validate(vaSp.Resource, vaSp.Code, vaSp.Expression, vaSp.Type, vaSp.Url, vaSp);
                    outcome.Issue.AddRange(issues);
                }
            }

            if (!outcome.Success)
            {
                Console.WriteLine($"    #---> Error validating search parameter {sp.Url}: {String.Join(",", sp.Base.Select(b => b.GetLiteral()))} - {sp.Code}");
                ReportOutcomeMessages(outcome);
                Console.WriteLine();
                return false;
            }
            return true;
        }

        const string diagnosticPrefix = "            ";
        private void ReportOutcomeMessages(OperationOutcome outcome)
        {
            foreach (var issue in outcome.Issue)
            {
                Console.WriteLine($"    *---> {issue.Severity?.GetLiteral()}: {issue.Details.Text}");
                if (!string.IsNullOrEmpty(issue.Diagnostics))
                {
                    var oldColor = Console.ForegroundColor;
                    var diag = issue.Diagnostics.Replace("\r\n\r\n", "\r\n").Trim();
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine($"{diagnosticPrefix}{diag.Replace("\r\n", "\r\n  " + diagnosticPrefix)}");
                    Console.ForegroundColor = oldColor;
                }
            }
        }

        private void AssertIsTrue(bool testResult, string message)
        {
            if (!testResult)
                Console.WriteLine($"{diagnosticPrefix}{message.Replace("\r\n", "\r\n  " + diagnosticPrefix)}");
        }
    }
}
