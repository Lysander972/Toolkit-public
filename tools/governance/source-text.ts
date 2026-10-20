export interface SourceRange {
  readonly start: number
  readonly end: number
}

export interface SourceFragment extends SourceRange {
  readonly content: string
}
