extern alias r4;
extern alias r4b;
extern alias r5;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Utility;

namespace UploadFIG
{

    internal static class DependencyChecker
    {
        public static void VerifyDependenciesOnServer(Settings settings, BaseFhirClient clientFhir, List<string> requiresCanonicals)
        {
            if (settings.TestPackageOnly)
                return;

            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            var oldColor = Console.ForegroundColor;
            foreach (var rawCanonical in requiresCanonicals.OrderBy(c => c))
            {
                var canonical = new Canonical(rawCanonical);
                var existing = clientFhir.Search<StructureDefinition>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search<ValueSet>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search<CodeSystem>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search("ConceptMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) > 0)
                {
                    var versionList = existing.Entry.Select(e => (e.Resource as IVersionableConformanceResource)?.Version).ToList();
                    if (settings.PreventDuplicateCanonicalVersions && versionList.Count > 1)
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t{string.Join(", ", versionList)}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\t{canonical.Uri}\t{canonical.Version ?? "(current)"}\t(missing)");
                }
                Console.ForegroundColor = oldColor;
            }
        }

        public static List<string> ScanForCanonicals(FHIRVersion fhirversion, List<Resource> resourcesToProcess, Common_Processor versionAgnosticProcessor)
        {
            List<string> requiresCanonicals = new List<string>();
            foreach (var resource in resourcesToProcess.OfType<StructureDefinition>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<ValueSet>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<CodeSystem>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR4(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.ConceptMap>())
            {
                ScanForCanonicalsR5(requiresCanonicals, resource);
            }

            // Now check for the ones that we've internally got covered :)
            foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>())
            {
                if (requiresCanonicals.Contains(resource.Url))
                    requiresCanonicals.Remove(resource.Url);
            }

            // And the types from the core resource profiles
            var coreCanonicals = requiresCanonicals.Where(v => Uri.IsWellFormedUriString(v, UriKind.Absolute) && versionAgnosticProcessor.ModelInspector.IsCoreModelTypeUri(new Uri(v))).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }

            // And check for any Core extensions (that are packaged in the standard zip package)
            CommonZipSource zipSource = null;
            if (fhirversion == FHIRVersion.N4_0)
                zipSource = r4::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4.zip"));
            else if (fhirversion == FHIRVersion.N4_3)
                zipSource = r4b::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4b.zip"));
            else if (fhirversion == FHIRVersion.N5_0)
                zipSource = r5::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r5.zip"));
            else
            {
                // version unhandled
                Console.WriteLine($"Unhandled processing of core extensions for fhir version {fhirversion}");
                return requiresCanonicals;
            }
            // ensure that the zip file is extracted correctly before using it
            zipSource.Prepare();

            // Scan for core/core extensions dependencies
            var coreSource = new CachedResolver(zipSource);
            var extensionCanonicals = requiresCanonicals.Where(v => coreSource.ResolveByCanonicalUri(v) != null).ToList();
            foreach (var coreCanonical in extensionCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }
            return requiresCanonicals;
        }

        record DependansOnCanonical
        {
            public DependansOnCanonical(string value)
            {
                CanonicalUrl = value;
            }

            public string CanonicalUrl { get; init; }
        }

        private static void CheckRequiresCanonical(Resource resource, string canonicalUrl, List<string> requiresCanonicals)
        {
            if (!string.IsNullOrEmpty(canonicalUrl))
            {
                if (!requiresCanonicals.Contains(canonicalUrl))
                    requiresCanonicals.Add(canonicalUrl);
                resource.AddAnnotation(new DependansOnCanonical(canonicalUrl));
            }
        }

        private static void ScanForCanonicalsR4(List<string> requiresCanonicals, r4.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(resource, group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<string> requiresCanonicals, r4b.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(resource, group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicalsR5(List<string> requiresCanonicals, r5.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, resource.SourceScope as Canonical ?? (resource.SourceScope as FhirUri)?.Value, requiresCanonicals);
            CheckRequiresCanonical(resource, resource.TargetScope as Canonical ?? (resource.TargetScope as FhirUri)?.Value, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            if (dependsOn.ValueSet != null)
                                CheckRequiresCanonical(resource, dependsOn.ValueSet, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            if (product.ValueSet != null)
                                CheckRequiresCanonical(resource, product.ValueSet, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.ValueSet != null)
                {
                    CheckRequiresCanonical(resource, group.Unmapped.ValueSet, requiresCanonicals);
                }
                if (group.Unmapped?.OtherMap != null)
                {
                    CheckRequiresCanonical(resource, group.Unmapped.OtherMap, requiresCanonicals);
                }
            }
        }


        private static void ScanForCanonicals(List<string> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(resource, resource.Supplements, requiresCanonicals);
            CheckRequiresCanonical(resource, resource.ValueSet, requiresCanonicals);
        }

        private static void ScanForCanonicals(List<string> requiresCanonicals, ValueSet resource)
        {
            foreach (var include in resource?.Compose?.Include)
            {
                CheckRequiresCanonical(resource, include.System, requiresCanonicals);
                foreach (var binding in include.ValueSet)
                {
                    CheckRequiresCanonical(resource, binding, requiresCanonicals);
                }
            }
            foreach (var exclude in resource?.Compose?.Exclude)
            {
                CheckRequiresCanonical(resource, exclude.System, requiresCanonicals);
                foreach (var binding in exclude.ValueSet)
                {
                    CheckRequiresCanonical(resource, binding, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<string> requiresCanonicals, StructureDefinition resource)
        {
            if (resource?.Differential?.Element == null)
            {
                // Nothing to process
                return;
            }

            foreach (var ed in resource?.Differential?.Element)
            {
                // Type bindings
                foreach (var t in ed.Type)
                {
                    foreach (var binding in t.Profile)
                    {
                        CheckRequiresCanonical(resource, binding, requiresCanonicals);
                    }
                    foreach (var binding in t.TargetProfile)
                    {
                        CheckRequiresCanonical(resource, binding, requiresCanonicals);
                    }
                }

                // Terminology Bindings
                CheckRequiresCanonical(resource, ed.Binding?.ValueSet, requiresCanonicals);
                if (ed.Binding?.Additional != null)
                {
                    foreach (var binding in ed.Binding?.Additional?.Select(a => a.ValueSet))
                    {
                        CheckRequiresCanonical(resource, binding, requiresCanonicals);
                    }
                }

                // value Alternatives
                foreach (var alternateExtension in ed.ValueAlternatives)
                {
                    CheckRequiresCanonical(resource, alternateExtension, requiresCanonicals);
                }
            }
        }
    }
}
