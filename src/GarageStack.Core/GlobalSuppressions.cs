using System.Diagnostics.CodeAnalysis;

// Entity defaults (CreatedAt = DateTime.UtcNow and the like) only fill in a row a caller did not date itself; every
// service, repository and endpoint takes its time from TimeProvider instead.
[assembly: SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Scope = "namespaceanddescendants",
    Target = "~N:GarageStack.Core.Models", Justification = "An entity default, not a decision about time")]
