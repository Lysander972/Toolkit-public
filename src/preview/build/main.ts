import { resolve } from 'node:path'
import { SiteFiles } from './SiteFiles.ts'

/** @anchor preview-assets */
export class PreviewBuild {
  private constructor() {}

  /** @anchor preview-assets */
  public static async run(): Promise<void> {
    const output: SiteFiles = new SiteFiles(resolve('_build/preview/site'))
    try {
      await output.write('.nojekyll', '')
    } finally {
      output.report()
    }
  }

  static {
    void PreviewBuild.run()
  }
}
