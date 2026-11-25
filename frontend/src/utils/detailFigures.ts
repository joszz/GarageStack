import type { Measure } from './units'

/** One tile of a detail's figures (DetailFigureList): a labelled value, with a line under it. */
export interface DetailFigure {
  key: string
  icon: string
  label: string
  /** Null when there is nothing to show: the tile reads "-". */
  measure: Measure | null
  /** What the figure means or where it comes from, on hover. */
  title?: string
  note?: { icon: string; text: string; title?: string } | null
}
