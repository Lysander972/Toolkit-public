import { Cli } from './Cli.ts'

/** @anchor cli-boundary */
export class Entrypoint {
  private constructor() {}

  static {
    Cli.run(process.argv.slice(2)).then(
      (code: number): void => {
        process.exitCode = code
      },
      (failure: unknown): void => {
        console.error(failure)
        process.exitCode = 1
      },
    )
  }
}
