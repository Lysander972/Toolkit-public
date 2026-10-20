import { createRoot } from 'react-dom/client'
import { Option } from 'effect'
import { PreviewPage } from './PreviewPage.tsx'
import '../../../res/frontend/styles/tk-tokens.css'
import '../../../res/preview/site.css'

/** @anchor preview-site */
export class PreviewBootstrap {
  private constructor() {}
  static {
    createRoot(Option.getOrThrow(Option.fromNullishOr(document.getElementById('root')))).render(
      <PreviewPage />,
    )
  }
}
