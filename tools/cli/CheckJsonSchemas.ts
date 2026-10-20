import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'

/** @anchor schema-mirror-integrity */
interface SchemaMirror {
  readonly relativePath: string
  readonly source: string
  readonly sha256: string
}

/** @anchor schema-mirror-integrity */
export class CheckJsonSchemas {
  private static readonly mirrors: readonly SchemaMirror[] = [
    {
      relativePath: 'res/schemas/package.json',
      source: 'https://www.schemastore.org/package.json',
      sha256: '76767b4e0b941f048f20d6de3eef2b8dd5f2b09816913a26b4bbf1afbd31fd79',
    },
    {
      relativePath: 'res/schemas/language-configuration.json',
      source: 'https://www.schemastore.org/language-configuration.json',
      sha256: '812a84433ed1983166f81b6d95435ccce096e7c63723ba3a4f3e32621a6dbff1',
    },
    {
      relativePath: 'res/schemas/tmlanguage.json',
      source: 'https://www.schemastore.org/tmlanguage.json',
      sha256: 'd5ed6d2d5c0fbd480d5184079e13c835b3997c2b03f6d0096a4506db55367cb9',
    },
  ]

  private constructor() {}

  /** @anchor schema-mirror-integrity */
  public static async run(repositoryFullPath: string): Promise<number> {
    const violations: string[] = []
    for (const mirror of CheckJsonSchemas.mirrors) {
      const response: Response = await fetch(mirror.source)
      if (!response.ok) {
        violations.push(`${mirror.source}: HTTP ${String(response.status)}`)
        continue
      }
      const remoteBytes: Buffer = Buffer.from(await response.arrayBuffer())
      const remoteHash: string = createHash('sha256').update(remoteBytes).digest('hex')
      if (remoteHash !== mirror.sha256) {
        violations.push(
          `${mirror.relativePath}: 远端 Schema 已变化，期望 ${mirror.sha256}，实际 ${remoteHash}`,
        )
        continue
      }
      const localBytes: Buffer = await readFile(join(repositoryFullPath, mirror.relativePath))
      const localHash: string = createHash('sha256').update(localBytes).digest('hex')
      if (localHash !== remoteHash)
        violations.push(`${mirror.relativePath}: 本地镜像与远端源不一致`)
    }
    for (const violation of violations) process.stderr.write(violation + '\n')
    return violations.length === 0 ? 0 : 1
  }
}
