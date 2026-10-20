import assert from 'node:assert/strict'
import { Option } from 'effect'
import { WorkspacePaths } from '../../src/toolkit/project_model/WorkspacePaths.ts'
import type { supportCodeLibraryBuilder } from '@cucumber/cucumber'

export interface RequirementSteps {
  readonly Before: typeof supportCodeLibraryBuilder.methods.Before
  readonly When: typeof supportCodeLibraryBuilder.methods.When
  readonly Then: typeof supportCodeLibraryBuilder.methods.Then
}

interface PathWorld {
  relativePath: string
  result: Option.Option<string>
  failure: Option.Option<unknown>
}

export class RequirementsSteps {
  private constructor() {}

  public static register(this: void, steps: RequirementSteps): void {
    steps.Before(function (this: PathWorld): void {
      this.result = Option.none()
      this.failure = Option.none()
    })
    steps.When('校验工程相对路径 {string}', function (this: PathWorld, relativePath: string): void {
      this.relativePath = relativePath
      try {
        this.result = Option.some(WorkspacePaths.require(relativePath))
      } catch (failure: unknown) {
        this.failure = Option.some(failure)
      }
    })
    steps.Then('路径校验成功并返回 {string}', function (this: PathWorld, expected: string): void {
      assert.equal(Option.isNone(this.failure), true)
      assert.equal(Option.getOrThrow(this.result), expected)
    })
    steps.Then('路径校验失败', function (this: PathWorld): void {
      assert.equal(Option.isNone(this.result), true)
      assert.equal(
        String(Option.getOrThrow(this.failure)),
        'Error: 非法工程相对路径: ' + this.relativePath,
      )
    })
  }
}
