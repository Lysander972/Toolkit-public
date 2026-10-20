/** @anchor asset-config-contract */
export type AssetCapability =
  | 'image-generation'
  | 'image-editing'
  | 'ui-texture'
  | 'video'
  | 'music'
  | 'sound-effect'
  | 'image-to-3d'

/** @anchor asset-config-contract */
type AssetModelResourceType = 'UNET' | 'CHECKPOINT' | 'LORA' | 'GGUF'

/** @anchor asset-config-contract */
type AssetEndpointTemplate =
  'https://www.runninghub.cn/proxy/{api_key}' | 'https://www.runninghub.cn/proxy-plus/{api_key}'

/** @anchor asset-config-contract */
export interface AssetModelBinding {
  readonly nodeId: string
  readonly inputName: string
  readonly nodeModelName: string
  readonly resourceType: AssetModelResourceType
  readonly createTime: string
  readonly favoriteEvidence: string
}

/** @anchor asset-config-contract */
export interface AssetWorkflowBinding {
  readonly capability: AssetCapability
  readonly workflowId: string
  readonly models: readonly AssetModelBinding[]
}

/** @anchor asset-config-contract */
export interface AssetGenerationConfig {
  readonly endpointTemplate: AssetEndpointTemplate
  readonly workflows: readonly AssetWorkflowBinding[]
}
