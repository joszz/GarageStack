// OSM `brand`/`operator` tags for fuel stations contain inconsistent spellings of the same
// chain (casing, "Express"/"Xpress" suffixes for shop-only sub-brands, abbreviated vs full
// company names). This maps each known raw variant to one canonical display name so the brand
// filter shows a single "BP" entry instead of "BP" / "BP express" side by side. Keys are
// lowercased+trimmed raw brand strings; anything not listed here keeps its own name unchanged.
const BRAND_ALIASES: Record<string, string> = {
  argosoil: 'Argos',
  avia: 'Avia',
  'avia marees': 'Avia',
  'avia truck': 'Avia',
  'avia xpress': 'Avia',
  'bp express': 'BP',
  'de meeuw': 'De Meeuw',
  'dirk vd broek': 'Dirk',
  'esso express': 'Esso',
  'fieten olie': 'Fieten Olie',
  'haan express': 'Haan',
  'lukoil express': 'Lukoil',
  'ok express': 'OK',
  'pin&go': 'Pin&Go',
  'shell express': 'Shell',
  't-energy express': 'T-Energy',
  'tamoil express': 'Tamoil',
  'texaco pouw bv': 'Texaco',
  'totalenergies express': 'TotalEnergies',
}

export function canonicalFuelBrand(raw: string): string {
  const trimmed = raw.trim()
  return BRAND_ALIASES[trimmed.toLowerCase()] ?? trimmed
}
