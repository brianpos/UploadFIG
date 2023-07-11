using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
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

        public void ValidateInvariants(StructureDefinition sd)
        {
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
                                VerifyInvariant(ed.Path, c.Key, c.Expression);
                            }
                        }
                    }
                }
            }
        }

        private void VerifyInvariant(string path, string key, string expression)
        {
            if (expression.Contains("descendants()"))
            {
                Console.WriteLine($"Fhirpath invariant testing does not support the descendants() function skipping this expression");
                return;
            }

            var visitor = new FhirPathExpressionVisitor();
            visitor.SetContext(path);
            var pe = _compiler.Parse(expression);
            var r = pe.Accept(visitor);

            if (!visitor.Outcome.Success || "boolean" != r.ToString())
            {
                // Console.WriteLine(visitor.ToString());
                AssertIsTrue(visitor.Outcome.Success, "Expected Invariant to pass");
                Console.WriteLine($"Context: {path}");
                Console.WriteLine($"Invariant key: {key}");
                Console.WriteLine($"Expression:\r\n{expression}");
                Console.WriteLine("---------");
                Console.WriteLine($"Result: {r}");
                Console.WriteLine("---------");
                ReportOutcomeMessages(visitor.Outcome);
                // Console.WriteLine(visitor.Outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
                AssertIsTrue("boolean" == r.ToString(), "Invariants must return a boolean");
            }
        }


        public void ValidateSearchExpression(SearchParameter sp)
        {
            foreach (var resource in sp.Base)
            {
                var spd = new ModelInfo.SearchParamDefinition()
                {
                    Resource = resource?.GetLiteral(),
                    Name = sp.Name,
                    Type = sp.Type ?? SearchParamType.Special, // Huh?
                    Expression = sp.Expression,
                    Url = sp.Url,

                };
                if (spd.Type == SearchParamType.Composite)
                {
                    // Populate the compisite fields
                }
                ValidateSearchExpression(spd, ModelInfo.SearchParameters);
            }
        }


        public void ValidateSearchExpression(ModelInfo.SearchParamDefinition spd, IEnumerable<ModelInfo.SearchParamDefinition> allSearchParams)
        {
            var visitor = new FhirPathExpressionVisitor();
            var t = _mi.GetTypeForFhirType(spd.Resource);
            if (t != null)
            {
                visitor.RegisterVariable("context", t);
                visitor.AddInputType(t);
                visitor.RegisterVariable("resource", t);
                VerifySearchExpression(t, spd.Expression, spd.Type, spd, visitor, allSearchParams);
            }
            else
            {
                // can't locate the type the search expression is trying to evaluate against
                Console.WriteLine($"Cannot resolve resource type {spd.Resource} for search {spd.Url}");
            }
        }

        private void ReportOutcomeMessages(OperationOutcome outcome)
        {
            foreach(var issue in outcome.Issue)
            {
                Console.WriteLine($"{issue.Severity?.GetLiteral()}: {issue.Details.Text}");
            }
        }

        private void VerifySearchExpression(Type resourceType, string expression, SearchParamType searchType, ModelInfo.SearchParamDefinition spd, FhirPathExpressionVisitor visitor, IEnumerable<ModelInfo.SearchParamDefinition> allSearchParams)
        {
            var pe = _compiler.Parse(expression);
            var r = pe.Accept(visitor);
            if (!visitor.Outcome.Success)
            {
                Console.WriteLine($"Context: {spd.Resource}");
                Console.WriteLine($"Search Param Name: {spd.Name}");
                Console.WriteLine($"Search Param Type: {spd.Type}");
                Console.WriteLine($"Expression:\r\n{spd.Expression}");
                Console.WriteLine($"Canonical:\r\n{spd.Url}");
                Console.WriteLine("---------");
                Console.WriteLine($"Result: {r}");
                Console.WriteLine("---------");
                // Console.WriteLine(visitor.ToString());
                ReportOutcomeMessages(visitor.Outcome);
                // Console.WriteLine(visitor.Outcome.ToXml(new FhirXmlSerializationSettings() { Pretty = true }));
            }
            // Assert.IsTrue(visitor.Outcome.Success == expectSuccessOutcome);

            // Assert.IsTrue(r.ToString().Length > 0);
            foreach (var returnType in r.ToString().Replace("[]", "").Split(", "))
            {
                switch (searchType)
                {
                    case SearchParamType.Number:
                        AssertIsTrue(NumberTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Date:
                        AssertIsTrue(DateTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.String:
                        AssertIsTrue(StringTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Token:
                        AssertIsTrue(TokenTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Reference:
                        AssertIsTrue(ReferenceTypes.Contains(returnType) || _mi.IsKnownResource(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Quantity:
                        AssertIsTrue(QuantityTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Uri:
                        AssertIsTrue(UriTypes.Contains(returnType), $"Search Type mismatch {searchType} type on {returnType} for {resourceType} - {spd.Name}");
                        break;
                    case SearchParamType.Composite:
                        // Need to feed this back into itself to verify
                        foreach (var cp in spd.Component)
                        {
                            // resolve the composite canonical to work out what type it should be
                            var componentSearchParameterType = allSearchParams.Where(sp => sp.Url == cp.Definition).FirstOrDefault()?.Type;
                            AssertIsTrue(componentSearchParameterType != null, $"Failed to resolve component URL: {cp.Definition} for {resourceType} - {spd.Name}");
                            foreach (var type in r.Types)
                            {
                                var visitorComponent = new FhirPathExpressionVisitor();
                                visitorComponent.RegisterVariable("resource", resourceType);
                                visitorComponent.RegisterVariable("context", type.ClassMapping);
                                visitorComponent.AddInputType(type.ClassMapping);
                                VerifySearchExpression(
                                    resourceType,
                                    cp.Expression,
                                    componentSearchParameterType.Value,
                                    null,
                                    visitorComponent,
                                    allSearchParams);
                            }
                        }
                        break;
                    case SearchParamType.Special:
                        // No real way to verify this special type
                        // Assert.Inconclusive($"Need to verify search {searchType} type on {returnType}");
                        break;
                }
            }
        }

        private void AssertIsTrue(bool testResult, string message)
        {
            if (!testResult)
                Console.WriteLine(message);
        }

        readonly string[] QuantityTypes = {
            "Quantity",
            "Money",
            "Range",
            "Duration",
            "Age",
        };

        readonly string[] TokenTypes = {
            "Identifier",
            "code",
            "CodeableConcept",
            "Coding",
            "string",
            "boolean",
            "id",
            "ContactPoint",
            "uri",
            "canonical",
        };

        readonly string[] StringTypes = {
            "markdown",
            "string",
            "Address",
            "HumanName",
        };

        readonly string[] NumberTypes = {
            "decimal",
            "integer",
        };

        readonly string[] ReferenceTypes = {
            "Reference",
            "canonical",
            "uri",
        };

        readonly string[] UriTypes = {
            "uri",
            "url",
            "canonical",
        };

        readonly string[] DateTypes = {
            "dateTime",
            "date",
            "Period",
            "instant",
            "Timing",
        };
    }
}
