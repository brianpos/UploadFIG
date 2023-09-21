using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;

namespace UploadFIG
{
    // Logging/reporting functionality associated with the expression validations
    internal class ExpressionValidatorBase
    {
        const string ErrorNamespace = "http://fhirpath-lab.com/CodeSystem/search-exp-errors";
        protected readonly static Coding InternalProcessingException = new(ErrorNamespace, "SE0001", "Unknown internal processing exception");
        protected readonly static Coding SearchCodeMissing = new(ErrorNamespace, "SE0101", "No 'code' property in search parameter");
        protected readonly static Coding SearchExpressionMissing = new(ErrorNamespace, "SE0102", "No 'expression' property in search parameter");

        protected void LogError(List<OperationOutcome.IssueComponent> results, OperationOutcome.IssueType issueType, Coding detail, string message, string diagnostics = null)
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
        protected void LogWarning(List<OperationOutcome.IssueComponent> results, OperationOutcome.IssueType issueType, Coding detail, string message, string diagnostics = null)
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

        const string diagnosticPrefix = "            ";
        protected void ReportOutcomeMessages(OperationOutcome outcome)
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

        protected void AssertIsTrue(bool testResult, string message)
        {
            if (!testResult)
                Console.WriteLine($"{diagnosticPrefix}{message.Replace("\r\n", "\r\n  " + diagnosticPrefix)}");
        }
    }
}