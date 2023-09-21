using Hl7.Fhir.FhirPath.Validator;
using Hl7.Fhir.Model;
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

        public ExpressionValidator(Common_Processor processor)
        {
            _processor = processor;

            // include all the conformance types
            _processor.ModelInspector.Import(typeof(StructureDefinition).Assembly);

            Hl7.Fhir.FhirPath.ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
            SymbolTable symbolTable = new(FhirPathCompiler.DefaultSymbolTable);
            _compiler = new FhirPathCompiler(symbolTable);
        }

        public abstract void PreValidation(List<Resource> resources);

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
            if (expression.Contains("descendants()"))
            {
                Console.WriteLine($"Warning: Fhirpath invariant testing does not support the descendants() function skipping this expression");
                return false;
            }

            var visitor = new BaseFhirPathExpressionVisitor(_processor.ModelInspector, _processor.SupportedResources, _processor.OpenTypes);
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
    }
}
