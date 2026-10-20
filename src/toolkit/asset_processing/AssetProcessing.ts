import { Context, Effect, Layer } from 'effect'
import type { CanvasKit } from 'canvaskit-wasm'
import type { GenerationArtifact } from '../asset_generation/GenerationContracts.ts'
import type { CanvasRenderer } from '../../canvas_render/CanvasRenderer.ts'
import type { Vector3 } from '../../canvas_render/CanvasTypesBase.ts'

/** @anchor asset-inspection-requirements */
export abstract class AssetRequirement {
  declare private readonly nominal: never
}

/** @anchor asset-inspection-requirements */
export type ImageFormat = 'png' | 'jpeg'

/** @anchor asset-inspection-requirements */
export class ImageRequirement extends AssetRequirement {
  declare private readonly imageNominal: never

  /** @anchor asset-inspection-requirements */
  public constructor(
    public readonly format: ImageFormat,
    public readonly width: number,
    public readonly height: number,
    public readonly requireTransparency: boolean,
  ) {
    super()
  }
}

/** @anchor asset-inspection-requirements */
export class AudioRequirement extends AssetRequirement {
  declare private readonly audioNominal: never

  /** @anchor asset-inspection-requirements */
  public constructor(
    public readonly format: string,
    public readonly durationSeconds: number,
    public readonly sampleRate: number,
    public readonly channels: number,
  ) {
    super()
  }
}

/** @anchor asset-inspection-requirements */
export class VideoRequirement extends AssetRequirement {
  declare private readonly videoNominal: never

  /** @anchor asset-inspection-requirements */
  public constructor(
    public readonly format: string,
    public readonly width: number,
    public readonly height: number,
    public readonly durationSeconds: number,
    public readonly frameRate: number,
  ) {
    super()
  }
}

/** @anchor asset-inspection-requirements */
export class ModelRequirement extends AssetRequirement {
  declare private readonly modelNominal: never

  /** @anchor asset-inspection-requirements */
  public constructor(public readonly files: readonly string[]) {
    super()
  }
}

/** @anchor asset-processing-contract */
export interface AssetInspection {
  readonly artifactId: string
  readonly requirement: AssetRequirement
  readonly diagnostics: readonly string[]
}

/** @anchor asset-processing-contract */
interface AssetSlice {
  readonly slicedBorderLB: Vector3
  readonly slicedBorderRT: Vector3
  readonly slicedRectLB: Vector3
  readonly slicedRectRT: Vector3
}

/** @anchor asset-processing-contract */
interface AssetSliceSize {
  readonly width: number
  readonly height: number
}

/** @anchor asset-processing-contract */
interface AssetSliceRequest {
  readonly artifact: GenerationArtifact
  readonly slice: AssetSlice
  readonly sizes: readonly AssetSliceSize[]
}

/** @anchor asset-processing-contract */
interface AssetSlicePreview {
  readonly artifactId: string
  readonly images: readonly Awaited<ReturnType<typeof CanvasRenderer.render>>[]
}

/** @anchor asset-processing-contract */
export interface AssetProcessing {
  /** @anchor asset-processing-contract */
  inspect(
    artifact: GenerationArtifact,
    requirement: AssetRequirement,
    signal: AbortSignal,
  ): Effect.Effect<AssetInspection, unknown>
  /** @anchor asset-processing-contract */
  renderSlice(
    request: AssetSliceRequest,
    backend: CanvasKit,
    signal: AbortSignal,
  ): Effect.Effect<AssetSlicePreview, unknown>
}

/** @anchor asset-processing-contract */
class PendingAssetProcessing implements AssetProcessing {
  declare private readonly nominal: never

  /** @anchor asset-processing-contract */
  public inspect(
    _artifact: GenerationArtifact,
    _requirement: AssetRequirement,
    _signal: AbortSignal,
  ): Effect.Effect<AssetInspection, unknown> {
    return Effect.fail(new Error('TODO: 解码具名产物并按明确要求校验格式与质量'))
  }

  /** @anchor asset-processing-contract */
  public renderSlice(
    _request: AssetSliceRequest,
    _backend: CanvasKit,
    _signal: AbortSignal,
  ): Effect.Effect<AssetSlicePreview, unknown> {
    return Effect.fail(new Error('TODO: 按显式切片和尺寸案例装配 Canvas 并复用 CPU renderer'))
  }
}

/** @anchor asset-processing-contract */
export class AssetProcessingService extends Context.Service<
  AssetProcessingService,
  AssetProcessing
>()('toolkit/AssetProcessing') {
  private constructor(service: never) {
    super(service)
  }

  public static readonly layer: Layer.Layer<AssetProcessingService> = Layer.effect(
    AssetProcessingService,
    Effect.sync((): AssetProcessing => new PendingAssetProcessing()),
  )
}
