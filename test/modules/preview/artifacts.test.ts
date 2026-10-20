import assert from 'node:assert/strict'
import { readdir, stat } from 'node:fs/promises'
import { extname } from 'node:path'
import { test } from 'vitest'
import { Option } from 'effect'

/** @anchor preview-browser */
export class PreviewArtifactsTest {
  private constructor() {}

  static {
    test('公开产物仅包含静态页面与站内资源', async (): Promise<void> => {
      const files: readonly string[] = await readdir('_build/preview/site', { recursive: true })
      const allowed: Readonly<Record<string, readonly string[]>> = {
        assets: ['.js', '.css', '.png', '.svg', '.ttf'],
        preview: ['.html', '.js', '.css', '.json', '.svg', '.ico', '.woff2'],
        renderer: ['.png', '.xml'],
        architecture: ['.html', '.js', '.css', '.svg', '.ico', '.txt'],
      }
      assert.ok(files.includes('index.html'))
      assert.ok(files.includes('.nojekyll'))
      for (const entry of files) {
        const file: string = entry.replaceAll('\\', '/')
        if (!(await stat('_build/preview/site/' + file)).isFile()) continue
        if (['index.html', '.nojekyll', 'architecture/robots.txt'].includes(file)) continue
        const area: string = Option.getOrThrow(Option.fromNullishOr(file.split('/')[0]))
        const extensions: readonly string[] = Option.getOrThrowWith(
          Option.fromNullishOr(allowed[area]),
          (): Error => new Error('公开产物目录越界：' + file),
        )
        assert.ok(extensions.includes(extname(file)), file)
        assert.notEqual(file, 'preview/project.json')
      }
    })
  }
}
