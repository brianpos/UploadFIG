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
        public static void VerifyDependenciesOnServer(Settings settings, BaseFhirClient clientFhir, List<CanonicalDetails> requiresCanonicals)
        {
            if (settings.TestPackageOnly)
                return;

            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            var oldColor = Console.ForegroundColor;
            foreach (var rawCanonical in requiresCanonicals.OrderBy(c => c.canonical))
            {
                var canonical = new Canonical(rawCanonical.canonical, rawCanonical.version, null);
                Bundle existing = null;
                switch (rawCanonical.resourceType)
                {
                    case "StructureDefinition":
                        existing = clientFhir.Search<StructureDefinition>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ValueSet":
                        existing = clientFhir.Search<ValueSet>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "CodeSystem":
                        existing = clientFhir.Search<CodeSystem>(new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "Questionnaire":
                        existing = clientFhir.Search("Questionnaire", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "StructureMap":
                        existing = clientFhir.Search("StructureMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                    case "ConceptMap":
                        existing = clientFhir.Search("ConceptMap", new[] { $"url={canonical.Uri}" }, null, null, SummaryType.True);
                        break;
                }
                if (existing == null || existing.Entry.Count(e => !(e.Resource is OperationOutcome)) > 0)
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

        public static List<CanonicalDetails> ScanForCanonicals(FHIRVersion fhirversion, List<Resource> resourcesToProcess, Common_Processor versionAgnosticProcessor)
        {
            List<CanonicalDetails> requiresCanonicals = new List<CanonicalDetails>();
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

            foreach (var resource in resourcesToProcess.OfType<r4.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR4(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r4b.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }
            foreach (var resource in resourcesToProcess.OfType<r5.Hl7.Fhir.Model.Questionnaire>())
            {
                ScanForCanonicalsR5(requiresCanonicals, resource);
            }

            // StructureMaps
            //      (structuredefintions and imports)
            //      (embedded ConceptMaps) `group.rule.target.where(transform='translate').parameter[1]` // the map URI.
            // Library - DataRequirements
            // PlanDefinitions
            // OperationDefinitions?

            // Now check for the ones that we've internally got covered :)
            foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>())
            {
                var node = requiresCanonicals.FirstOrDefault(rc => rc.resourceType == (resource as Resource).TypeName && rc.canonical == resource.Url);
                if (node != null)
                {
                    requiresCanonicals.Remove(node);
                }
            }

            // And the types from the core resource profiles
            var coreCanonicals = requiresCanonicals.Where(v => Uri.IsWellFormedUriString(v.canonical, UriKind.Absolute) && versionAgnosticProcessor.ModelInspector.IsCoreModelTypeUri(new Uri(v.canonical))).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }

            // And check for any Core extensions (that are packaged in the standard zip package)
            CommonZipSource zipSource = null;
            if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_0.GetLiteral()))
                zipSource = r4::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4.zip"));
            else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N4_3.GetLiteral()))
                zipSource = r4b::Hl7.Fhir.Specification.Source.ZipSource.CreateValidationSource(Path.Combine(CommonDirectorySource.SpecificationDirectory, "specification.r4b.zip"));
            else if (fhirversion.GetLiteral().StartsWith(FHIRVersion.N5_0.GetLiteral()))
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
            var extensionCanonicals = requiresCanonicals.Where(v => coreSource.ResolveByCanonicalUri(v.canonical) != null).ToList();
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

        private static void CheckRequiresCanonical(Resource resource, string canonicalType, string canonicalUrl, List<CanonicalDetails> requiresCanonicals)
        {
            if (!string.IsNullOrEmpty(canonicalUrl))
            {
                if (canonicalUrl.StartsWith("#") && resource is DomainResource dr)
                {
                    // local reference - check that it exists in the resource
                    var localRef = dr.Contained?.Where(c => c.Id == canonicalUrl.Substring(1));
                    if (!localRef.Any())
                        Console.WriteLine($"Unable to resolve contained canonical in {resource.TypeName}/{resource.Id}: {canonicalUrl}");
                    return;
                }

                Canonical c = new Canonical(canonicalUrl);
                if (!requiresCanonicals.Any(s => s.canonical == c.Value && s.resourceType == canonicalType))
                    requiresCanonicals.Add(new CanonicalDetails()
                    {
                        canonical = c.Value,
                        version = c.Version,
                        resourceType = canonicalType,
                    });
                resource.AddAnnotation(new DependansOnCanonical(canonicalUrl));
            }
        }

        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped?.Url != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.ConceptMap resource)
        {
            CheckRequiresCanonical(resource, "ValueSet", resource.Source as Canonical, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.Target as Canonical, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    foreach (var target in element.Target)
                    {
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", dependsOn.System, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "CodeSystem", product.System, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.Url, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.ConceptMap resource)
        {
            foreach (var prop in resource.Property)
            {
                CheckRequiresCanonical(resource, "CodeSystem", prop.System, requiresCanonicals);
            }
            CheckRequiresCanonical(resource, "ValueSet", resource.SourceScope as Canonical ?? (resource.SourceScope as FhirUri)?.Value, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.TargetScope as Canonical ?? (resource.TargetScope as FhirUri)?.Value, requiresCanonicals);

            foreach (var group in resource.Group)
            {
                CheckRequiresCanonical(resource, "CodeSystem", group.Source, requiresCanonicals);
                CheckRequiresCanonical(resource, "CodeSystem", group.Target, requiresCanonicals);

                foreach (var element in group.Element)
                {
                    CheckRequiresCanonical(resource, "ValueSet", element.ValueSet, requiresCanonicals);

                    foreach (var target in element.Target)
                    {
                        CheckRequiresCanonical(resource, "ValueSet", target.ValueSet, requiresCanonicals);
                        foreach (var dependsOn in target.DependsOn)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", dependsOn.ValueSet, requiresCanonicals);
                        }
                        foreach (var product in target.Product)
                        {
                            CheckRequiresCanonical(resource, "ValueSet", product.ValueSet, requiresCanonicals);
                        }
                    }
                }
                if (group.Unmapped != null)
                {
                    CheckRequiresCanonical(resource, "ValueSet", group.Unmapped.ValueSet, requiresCanonicals);
                    CheckRequiresCanonical(resource, "ConceptMap", group.Unmapped.OtherMap, requiresCanonicals);
                }
            }
        }


        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(resource, "CodeSystem", resource.Supplements, requiresCanonicals);
            CheckRequiresCanonical(resource, "ValueSet", resource.ValueSet, requiresCanonicals);
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, ValueSet resource)
        {
            foreach (var include in resource?.Compose?.Include)
            {
                CheckRequiresCanonical(resource, "CodeSystem", include.System, requiresCanonicals);
                foreach (var binding in include.ValueSet)
                {
                    CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                }
            }
            foreach (var exclude in resource?.Compose?.Exclude)
            {
                CheckRequiresCanonical(resource, "CodeSystem", exclude.System, requiresCanonicals);
                foreach (var binding in exclude.ValueSet)
                {
                    CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                }
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, StructureDefinition resource)
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
                        CheckRequiresCanonical(resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                    foreach (var binding in t.TargetProfile)
                    {
                        CheckRequiresCanonical(resource, "StructureDefinition", binding, requiresCanonicals);
                    }
                }

                // Terminology Bindings
                CheckRequiresCanonical(resource, "ValueSet", ed.Binding?.ValueSet, requiresCanonicals);
                if (ed.Binding?.Additional != null)
                {
                    foreach (var binding in ed.Binding?.Additional?.Select(a => a.ValueSet))
                    {
                        CheckRequiresCanonical(resource, "ValueSet", binding, requiresCanonicals);
                    }
                }

                // value Alternatives
                foreach (var alternateExtension in ed.ValueAlternatives)
                {
                    CheckRequiresCanonical(resource, "StructureDefinition", alternateExtension, requiresCanonicals);
                }
            }
        }


        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, r4.Hl7.Fhir.Model.Questionnaire resource)
        {
            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR4(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicalsR4(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR4(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, r4b.Hl7.Fhir.Model.Questionnaire resource)
        {
            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicals(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r4b.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicals(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, r5.Hl7.Fhir.Model.Questionnaire resource)
        {
            foreach (var derivedFrom in resource.DerivedFrom)
                CheckRequiresCanonical(resource, "Questionnaire", derivedFrom, requiresCanonicals);

            ScanForSDCExtensionCanonicals(requiresCanonicals, resource);

            ScanForCanonicalsR5(requiresCanonicals, resource, resource.Item);
        }

        private static void ScanForCanonicalsR5(List<CanonicalDetails> requiresCanonicals, Resource resource, List<r5.Hl7.Fhir.Model.Questionnaire.ItemComponent> items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                CheckRequiresCanonical(resource, "ValueSet", item.AnswerValueSet, requiresCanonicals);

                ScanForSDCItemExtensionCanonicals(requiresCanonicals, resource, item);
                ScanForCanonicalsR5(requiresCanonicals, resource, item.Item);
            }
        }

        private static void ScanForSDCExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, DomainResource resource)
        {
            // SDC extras
            CheckRequiresCanonical(resource, "StructureMap", resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-targetStructureMap"), requiresCanonicals);
            CheckRequiresCanonical(resource, "Questionnaire", resource.GetExtensionValue<Canonical>("http://hl7.org/fhir/uv/sdc/StructureDefinition/sdc-questionnaire-subQuestionnaire"), requiresCanonicals);
        }

        private static void ScanForSDCItemExtensionCanonicals(List<CanonicalDetails> requiresCanonicals, Resource resource, Element item)
        {
            // Maybe think some more about if we can dynamically also scan the extensions from the definitions and "discover" more...

            // SDC extras
            CheckRequiresCanonical(resource, "ValueSet", item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-unitValueSet"), requiresCanonicals);
            CheckRequiresCanonical(resource, "StructureDefinition", item.GetExtensionValue<Canonical>("http://hl7.org/fhir/StructureDefinition/questionnaire-referenceProfile"), requiresCanonicals);
        }
    }
}
