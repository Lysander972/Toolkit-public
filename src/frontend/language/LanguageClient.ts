import type {
  LanguageClient as StandardLanguageClient,
  LanguageClientOptions,
} from 'vscode-languageclient/node.js'
import type { Host } from '@toolkit/host'

/** @anchor frontend-language-contract */
export class LanguageClient {
  private constructor() {}

  /** @anchor frontend-language-contract */
  public static connect(
    _host: Host,
    _options: LanguageClientOptions,
  ): Promise<StandardLanguageClient> {
    return Promise.reject(new Error('TODO: 标准只读 LSP 进程内连接、Host 查询适配与客户端释放'))
  }
}
