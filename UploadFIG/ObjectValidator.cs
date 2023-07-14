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
    public class FhirPathExpressionVisitor : BaseFhirPathExpressionVisitor
    {
        public FhirPathExpressionVisitor()
            : base(ModelInspector.ForAssembly(typeof(Patient).Assembly),
                  Hl7.Fhir.Model.ModelInfo.SupportedResources,
                  Hl7.Fhir.Model.ModelInfo.OpenTypes)
        {
        }
    }

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

            if (!visitor.Outcome.Success || "boolean" != r.ToString())
            {
                Console.WriteLine($"    #---> Error validating invariant {canonicalUrl}: {key}");
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
                SearchExpressionValidator v = new SearchExpressionValidator(_mi,
                      Hl7.Fhir.Model.ModelInfo.SupportedResources,
                      Hl7.Fhir.Model.ModelInfo.OpenTypes,
                      (url) =>
                      {
                          return ModelInfo.SearchParameters.Where(sp => sp.Url == url)
                          .Select(v => ToVaSpd(v))
                          .FirstOrDefault();
                      });
                v.IncludeParseTreeDiagnostics = true;
                var issues = v.Validate(vaSp.Resource, vaSp.Code, vaSp.Expression, vaSp.Type, vaSp.Url, vaSp);
                outcome.Issue.AddRange(issues);
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
            foreach(var issue in outcome.Issue)
            {
                Console.WriteLine($"    *---> {issue.Severity?.GetLiteral()}: {issue.Details.Text}");
                if (!string.IsNullOrEmpty(issue.Diagnostics))
                {
                    var diag = issue.Diagnostics.Replace("\r\n\r\n", "\r\n").Trim();
                    Console.WriteLine($"{diagnosticPrefix}{diag.Replace("\r\n", "\r\n  " + diagnosticPrefix)}");
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
