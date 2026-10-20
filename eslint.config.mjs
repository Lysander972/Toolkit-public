import { register } from 'tsx/esm/api'
import { Option } from 'effect'

const loader = register({ namespace: 'toolkit-eslint' })
const [{ CodebaseConfiguration }, { ArchitectureProject }, { WorkspaceFiles }] = await Promise.all([
  loader.import('./tools/lint/style/eslint.config.ts', import.meta.url),
  loader.import('./tools/cli/ArchitectureProject.ts', import.meta.url),
  loader.import('./tools/cli/WorkspaceFiles.ts', import.meta.url),
]).finally(loader.unregister)

const model = await ArchitectureProject.load(import.meta.dirname, Option.none())

export default CodebaseConfiguration.create(
  WorkspaceFiles.implementationTypeScript(model, import.meta.dirname),
)
