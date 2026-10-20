import { test } from 'vitest'
import assert from 'node:assert/strict'
import fc from 'fast-check'
import { FloatText } from '../../../src/toolkit/canvas/FloatText.ts'

/** @anchor canvas-float */
export class FloatTextPropertyTest {
  private constructor() {}

  static {
    test('有限 float32 十进制往返保持 IEEE754 原始位', (): void => {
      fc.assert(
        fc.property(fc.float({ noNaN: true, noDefaultInfinity: true }), (value: number): void => {
          const expected: DataView = new DataView(new ArrayBuffer(4))
          expected.setFloat32(0, value, true)
          const actual: DataView = new DataView(new ArrayBuffer(4))
          actual.setFloat32(0, FloatText.parse(FloatText.format(value), 'value'), true)
          assert.equal(actual.getUint32(0, true), expected.getUint32(0, true))
        }),
        { numRuns: 2000 },
      )
    })
  }
}
