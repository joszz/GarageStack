using System.Diagnostics.CodeAnalysis;

// Demo mode lays its sample car, trips and logs out around the real clock on purpose: a visitor sees
// "today" and "5 minutes ago" whenever they look.
[assembly: SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Scope = "namespaceanddescendants",
    Target = "~N:GarageStack.Data.Demo", Justification = "Demo data is placed relative to the wall clock by design")]
