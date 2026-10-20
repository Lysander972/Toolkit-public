import type { CanvasDocument } from '@toolkit/canvas/model'
import type { Option } from 'effect'
import type { CanvasColor, UIComponentType } from './CanvasTypesBase.ts'

/** @anchor canvas-render-cpu-boundary */
export interface CanvasViewport {
  readonly width: number
  readonly height: number
}

/** @anchor canvas-render-cpu-boundary */
type CanvasResourceKind = 'image' | 'spine-atlas' | 'spine-binary' | 'spine-json'

/** @anchor canvas-render-cpu-boundary */
interface CanvasResource {
  readonly guid: string
  readonly sourceRelativePath: string
  readonly revision: string
  readonly kind: CanvasResourceKind
  readonly bytes: Uint8Array
}

/**
 * @anchor canvas-render-cpu-boundary
 * @anchor canvas-render-resource-build
 */
export interface CanvasFont {
  readonly name: string
  readonly revision: string
  readonly bytes: Uint8Array
}

/**
 * @anchor canvas-render-cpu-boundary
 * @anchor canvas-render-guid-resolution
 * @anchor canvas-render-guid-preview-modes
 */
export interface CanvasRenderInput {
  readonly document: CanvasDocument
  readonly viewport: CanvasViewport
  readonly resources: readonly CanvasResource[]
  readonly palette: ReadonlyMap<string, CanvasColor>
  readonly fonts: readonly CanvasFont[]
  readonly timeSeconds: number
}

/**
 * @anchor canvas-render-cpu-boundary
 * @anchor canvas-render-error-report
 */
interface CanvasRenderDiagnostic {
  readonly code: string
  readonly message: string
  readonly nodeIndices: readonly number[]
  readonly resourceGuid: Option.Option<string>
  readonly sourceRelativePath: Option.Option<string>
}

/** @anchor canvas-render-cpu-boundary */
interface CanvasRenderPoint {
  readonly x: number
  readonly y: number
}

/** @anchor canvas-render-cpu-boundary */
interface CanvasRenderNode {
  readonly nodeIndices: readonly number[]
  readonly name: string
  readonly type: UIComponentType
  readonly corners: readonly [
    CanvasRenderPoint,
    CanvasRenderPoint,
    CanvasRenderPoint,
    CanvasRenderPoint,
  ]
}

/** @anchor canvas-render-cpu-boundary */
export interface CanvasRenderResult {
  readonly png: Uint8Array
  readonly width: number
  readonly height: number
  readonly nodes: readonly CanvasRenderNode[]
  readonly resourceFingerprint: string
  readonly diagnostics: readonly CanvasRenderDiagnostic[]
}

/** @anchor canvas-render-rich-text */
export interface CanvasTextRun {
  readonly text: string
  readonly size: Option.Option<number>
  readonly color: Option.Option<CanvasColor>
}
