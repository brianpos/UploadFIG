﻿using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UploadFIG
{

    internal static class DependencyChecker
    {
        public static void VerifyDependenciesOnServer(Settings settings, FhirClient clientFhir, Dictionary<string, bool> requiresCanonicals)
        {
            if (settings.TestPackageOnly)
                return;

            Console.WriteLine("");
            Console.WriteLine("Destination server canonical resource dependency verification:");
            // Verify that the set of canonicals are available on the server
            var oldColor = Console.ForegroundColor;
            foreach (var rawCanonical in requiresCanonicals.Keys)
            {
                var canonical = new Canonical(rawCanonical);
                // profiles
                var existing = clientFhir.Search<StructureDefinition>(new[] { $"url={canonical.Uri}" });
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search<ValueSet>(new[] { $"url={canonical.Uri}" });
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search<CodeSystem>(new[] { $"url={canonical.Uri}" });
                if (existing.Entry.Count(e => !(e.Resource is OperationOutcome)) == 0)
                    existing = clientFhir.Search<ConceptMap>(new[] { $"url={canonical.Uri}" });
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

        public static void ScanForCanonicals(List<Resource> resourcesToProcess, Dictionary<string, bool> requiresCanonicals)
        {
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

            foreach (var resource in resourcesToProcess.OfType<ConceptMap>())
            {
                ScanForCanonicals(requiresCanonicals, resource);
            }

            // Now check for the ones that we've internally got covered :)
            foreach (var resource in resourcesToProcess.OfType<IVersionableConformanceResource>())
            {
                if (requiresCanonicals.ContainsKey(resource.Url))
                    requiresCanonicals.Remove(resource.Url);
            }

            // And the types from the core resource profiles
            var coreCanonicals = requiresCanonicals.Keys.Where(v => Uri.IsWellFormedUriString(v, UriKind.Absolute) && ModelInfo.IsCoreModelTypeUri(new Uri(v))).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }

            // And check for any Core extensions (that are packaged in the standard zip pacakge)
            var coreSource = new CachedResolver(ZipSource.CreateValidationSource());
            var extensionCanonicals = requiresCanonicals.Keys.Where(v => coreSource.ResolveByCanonicalUri(v) != null).ToList();
            foreach (var coreCanonical in coreCanonicals)
            {
                requiresCanonicals.Remove(coreCanonical);
            }
        }

        record DependansOnCanonical
        {
            public DependansOnCanonical(string value)
            {
                CanonicalUrl = value;
            }

            public string CanonicalUrl { get; init; }
        }

        private static void CheckRequiresCanonical(Resource resource, string canonicalUrl, Dictionary<string, bool> requiresCanonicals)
        {
            if (!string.IsNullOrEmpty(canonicalUrl))
            {
                if (!requiresCanonicals.ContainsKey(canonicalUrl))
                    requiresCanonicals.Add(canonicalUrl, false);
                resource.AddAnnotation(new DependansOnCanonical(canonicalUrl));
            }
        }

        private static void ScanForCanonicals(Dictionary<string, bool> requiresCanonicals, ConceptMap resource)
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

        private static void ScanForCanonicals(Dictionary<string, bool> requiresCanonicals, CodeSystem resource)
        {
            CheckRequiresCanonical(resource, resource.Supplements, requiresCanonicals);
            CheckRequiresCanonical(resource, resource.ValueSet, requiresCanonicals);
        }

        private static void ScanForCanonicals(Dictionary<string, bool> requiresCanonicals, ValueSet resource)
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

        private static void ScanForCanonicals(Dictionary<string, bool> requiresCanonicals, StructureDefinition resource)
        {
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