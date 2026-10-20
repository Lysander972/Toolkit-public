import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { Git } from '../cli/Git.ts'

export interface GitResult {
  readonly status: number
  readonly stdout: string
  readonly stderr: string
}

export class GitFixture {
  private constructor() {}

  public static create(repositoryFullPath: string): void {
    // @non-manual-path 独占 Git 测试夹具的源码目录，不枚举工作区。
    mkdirSync(join(repositoryFullPath, 'tools/code'), { recursive: true })
    mkdirSync(join(repositoryFullPath, 'specs'))
    Git.run(repositoryFullPath, ['init', '--initial-branch=main'])
    Git.run(repositoryFullPath, ['config', 'user.name', 'Toolkit Fixture'])
    Git.run(repositoryFullPath, ['config', 'user.email', 'fixture@example.invalid'])
    Git.run(repositoryFullPath, ['config', 'core.autocrlf', 'false'])
    writeFileSync(join(repositoryFullPath, '.gitignore'), '_build/\n')
    // @non-manual-path 独占 Git 测试夹具的架构输入，不枚举工作区。
    writeFileSync(
      join(repositoryFullPath, 'tools/model.c4'),
      "specification { element system\n element container\n tag tool\n tag doc }\nmodel { app = system 'app' { description '治理入口' metadata { sourcePath 'tools' checks 'selfcheck' }\n code = container 'code' { #tool description '受检源码' metadata { sourcePath 'tools/code' files ['tools/code/*.ts'] } } }\n specs = system 'specs' { description '待迁移合同' document = container 'document' { #doc description '合同正文' metadata { specPath 'specs' files ['specs/*.md'] } } } }\n",
    )
    // @non-manual-path 独占 Git 测试夹具的源码输入，不枚举工作区。
    writeFileSync(
      join(repositoryFullPath, 'tools/code/alpha.ts'),
      'export function alpha(): number { return 1 }\n',
    )
    writeFileSync(join(repositoryFullPath, 'specs/spec.md'), '# Contract\n')
    Git.run(repositoryFullPath, ['add', '.'])
    Git.run(repositoryFullPath, ['-c', 'commit.gpgsign=false', 'commit', '-m', 'test: fixture'])
  }
}
