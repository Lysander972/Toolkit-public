import assert from 'node:assert/strict'
import { test } from 'vitest'
import { DOMParser, XMLSerializer, onErrorStopParsing } from '@xmldom/xmldom'
import type { Document } from '@xmldom/xmldom'
import { Option } from 'effect'
import { CanvasConverter } from '../../../src/toolkit/canvas/CanvasConverter.ts'
import { CanvasSchema } from '../../../src/toolkit/canvas/CanvasSchema.ts'
import { CanvasTags, HtmlKind } from '../../../src/toolkit/canvas/CanvasTags.ts'
import type { HtmlTag } from '../../../src/toolkit/canvas/CanvasTags.ts'
import type { FieldKind } from '../../../src/toolkit/canvas/CanvasSchema.ts'
import { FloatText } from '../../../src/toolkit/canvas/FloatText.ts'
import { CanvasBinary } from '../../../src/toolkit/canvas/CanvasBinary.ts'
import {
  BoolVar,
  CanvasDocument,
  CanvasNode,
  ColorVar,
  FloatVar,
  ListVar,
  LongVar,
  StringVar,
  Vector3Var,
} from '../../../src/toolkit/canvas/CanvasValues.ts'
import type { Var } from '../../../src/toolkit/canvas/CanvasValues.ts'

/**
 * @anchor canvas-convert
 * @anchor canvas-view-precision-readability
 */
export class CanvasConversionTest {
  private constructor() {}

  static {
    test('四种形态汇合到同一文件字节', (): void => {
      CanvasConversionTest.converges()
    })
    test('浮点与颜色通道按位还原', (): void => {
      CanvasConversionTest.floats()
    })
    test('HTML 规范化自闭合、枚举和文档壳', (): void => {
      CanvasConversionTest.html()
    })
    test('完整 HTML 的展示资源与脚本不进入文档，编辑子树后取得新布局', (): void => {
      const html: Document = new DOMParser({ onError: onErrorStopParsing }).parseFromString(
        '<html><head><script><![CDATA[throw new Error("展示脚本不执行 <BeCanvas>")]]></script></head><body><img src="missing-preview.png" /><section hidden="hidden"><BeCanvas name="布局" width="1080" height="2160"><BeUIButton name="按钮" label="原文本" clickcbname="点击"></BeUIButton></BeCanvas></section></body></html>',
        'application/xml',
      )
      const serializer: XMLSerializer = new XMLSerializer()
      const original: CanvasDocument = CanvasConverter.fromHtml(serializer.serializeToString(html))
      const expectedRoot: CanvasNode = CanvasNode.create('Canvas')
      const expectedButton: CanvasNode = CanvasConversionTest.named('UIButton', [
        CanvasConversionTest.pair('label', StringVar.of('原文本')),
        CanvasConversionTest.pair('clickCBName', StringVar.of('点击')),
      ])
      expectedButton.setField('name', StringVar.of('按钮'))
      expectedRoot.addChild(expectedButton)
      const expected: CanvasDocument = CanvasDocument.of('布局', expectedRoot)
      assert.deepEqual(CanvasConverter.toBinary(original), CanvasConverter.toBinary(expected))
      Option.getOrThrow(
        Option.fromNullishOr(html.getElementsByTagName('BeUIButton').item(0)),
      ).setAttribute('label', '编辑后文本')
      const edited: CanvasDocument = CanvasConverter.fromHtml(serializer.serializeToString(html))
      expectedButton.setField('label', StringVar.of('编辑后文本'))
      assert.deepEqual(CanvasConverter.toBinary(edited), CanvasConverter.toBinary(expected))
      assert.notDeepEqual(CanvasConverter.toBinary(edited), CanvasConverter.toBinary(original))
    })
    test('RGBA 使用严格十进制并在归一化后舍入', (): void => {
      for (const text of ['0', '-0', '255', '25.500001', '2.55e1']) {
        const document: CanvasDocument = CanvasConverter.fromHtml(
          `<BeCanvas name="color" width="1080" height="2160"><BeUIRect name="rect" color="rgba(${text},0,0,255)"></BeUIRect></BeCanvas>`,
        )
        const node: CanvasNode = Option.getOrThrow(
          Option.fromNullishOr(document.root.children()[0]),
        )
        const color: Var = Option.getOrThrow(node.field('color'))
        assert.ok(color instanceof ColorVar)
        assert.equal(FloatText.bits(color.red), FloatText.bits(Math.fround(Number(text) / 255)))
      }
      for (const text of [
        '0x10',
        ' 1',
        '1 ',
        '+1',
        '.1',
        '1.',
        'NaN',
        'Infinity',
        '1e400',
        '-0.1',
        '255.000001',
      ]) {
        assert.throws((): CanvasDocument =>
          CanvasConverter.fromHtml(
            `<BeCanvas name="color" width="1080" height="2160"><BeUIRect name="rect" color="rgba(${text},0,0,255)"></BeUIRect></BeCanvas>`,
          ),
        )
      }
    })
    test('字段、枚举和标签表拒绝封闭集合之外的输入', (): void => {
      CanvasConversionTest.closed()
    })
    test('损坏输入直接失败', (): void => {
      CanvasConversionTest.rejects()
    })
    test('Base64 与原生编码一致，并拒绝宽松解码和非零填充位', (): void => {
      for (const text of ['', 'a', 'ab', 'abc']) {
        const root: CanvasNode = CanvasNode.create('Canvas')
        root.addChild(
          CanvasConversionTest.named('UILabel', [
            CanvasConversionTest.pair('text', StringVar.of(text)),
          ]),
        )
        const document: CanvasDocument = CanvasDocument.of('base64', root)
        const payload: string = Buffer.from(CanvasBinary.toInlineBytes(document.root)).toString(
          'base64',
        )
        assert.equal(CanvasConverter.toInlineBase64(document), 'canvas:base64=' + payload)
        assert.deepEqual(
          CanvasConverter.toBinary(CanvasConverter.fromInline('canvas:base64=' + payload)),
          CanvasConverter.toBinary(document),
        )
        assert.throws(
          (): CanvasDocument => CanvasConverter.fromInline('canvas:base64=' + payload + '\r\n'),
          /Base64 不是标准编码/,
        )
      }
      const empty: CanvasDocument = CanvasDocument.of('base64', CanvasNode.create('Canvas'))
      assert.equal(CanvasConverter.toInlineBase64(empty), 'canvas:base64=EAAA')
      assert.throws(
        (): CanvasDocument => CanvasConverter.fromInline('canvas:base64=EAB='),
        /Base64 不是标准编码/,
      )
      for (const payload of ['', 'EAA', 'EAA==', 'E*A=', 'EA=A', 'EAA= ']) {
        assert.throws((): CanvasDocument => CanvasConverter.fromInline('canvas:base64=' + payload))
      }
    })
    test('HTML 标签字段全部落在 Schema 封闭集合内', (): void => {
      CanvasConversionTest.htmlSchemaMatrix()
    })
    test('多行文本经过 HTML 后保持原文件字节', (): void => {
      const root: CanvasNode = CanvasNode.create('Canvas')
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('第一行\n第二行\t末尾\r')),
        ]),
      )
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('null')),
        ]),
      )
      const document: CanvasDocument = CanvasDocument.of('multiline', root)
      assert.deepEqual(
        CanvasConverter.toBinary(CanvasConverter.fromHtml(CanvasConverter.toHtmlInline(document))),
        CanvasConverter.toBinary(document),
      )
    })
    test('Hjson 字符串保持回车和语法边界字节', (): void => {
      const root: CanvasNode = CanvasNode.create('Canvas')
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('#tag\r\n{literal}\t')),
        ]),
      )
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('a\rb')),
        ]),
      )
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('},x')),
        ]),
      )
      root.addChild(
        CanvasConversionTest.named('UILabel', [
          CanvasConversionTest.pair('text', StringVar.of('\'q"r')),
        ]),
      )
      for (const text of [
        '\0\b\f',
        'null',
        'true',
        '123',
        ' leading ',
        '\\u201c',
        '\u2028\u2029',
      ]) {
        root.addChild(
          CanvasConversionTest.named('UILabel', [
            CanvasConversionTest.pair('text', StringVar.of(text)),
          ]),
        )
      }
      const document: CanvasDocument = CanvasDocument.of('hjson-boundaries', root)
      const binary: Uint8Array = CanvasConverter.toBinary(document)
      assert.deepEqual(
        CanvasConverter.toBinary(
          CanvasConverter.fromInline(CanvasConverter.toInlineHjson(document)),
        ),
        binary,
      )
    })
    test('超出 float32 范围的 HTML 数值直接失败', (): void => {
      assert.throws((): CanvasDocument =>
        CanvasConverter.fromHtml(
          '<BeCanvas name="overflow" width="1080" height="2160"><BeUIRect name="rect" rot="1e39"></BeUIRect></BeCanvas>',
        ),
      )
    })
    test('写出二进制时拒绝节点未知字段', (): void => {
      const root: CanvasNode = CanvasNode.create('Canvas')
      root.addChild(
        CanvasConversionTest.named('UIRect', [
          CanvasConversionTest.pair('unknownField', StringVar.of('value')),
        ]),
      )
      assert.throws((): Uint8Array => CanvasConverter.toBinary(CanvasDocument.of('invalid', root)))
    })
    test('二进制与 HTML 拒绝非有限浮点和越界颜色', (): void => {
      assert.throws((): FloatVar => FloatVar.of(Infinity))
      assert.throws((): ColorVar => ColorVar.rgba(1.01, 0, 0, 1))
      assert.throws((): CanvasDocument =>
        CanvasConverter.fromHtml(
          '<BeCanvas name="color" width="1080" height="2160"><BeUIRect name="rect" color="rgba(256,0,0,255)"></BeUIRect></BeCanvas>',
        ),
      )
    })
    test('二进制拒绝变量位置上的 UI 类型标记', (): void => {
      assert.throws((): CanvasDocument =>
        CanvasConverter.fromBinary(
          Uint8Array.from([0x02, 0x76, 0x31, 0x01, 0x6e, 0x10, 0x01, 0x11, 0x03, 0x00, 0x04]),
        ),
      )
    })
  }

  /** @anchor canvas-convert */
  private static converges(): void {
    const document: CanvasDocument = CanvasConversionTest.sample()
    const binary: Uint8Array = CanvasConverter.toBinary(document)
    assert.deepEqual(CanvasConverter.toBinary(CanvasConverter.fromBinary(binary)), binary)
    assert.deepEqual(
      CanvasConverter.toBinary(
        CanvasConverter.fromInline(CanvasConverter.toInlineBase64(document)),
      ),
      binary,
    )
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromInline(CanvasConverter.toInlineHjson(document))),
      binary,
    )
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromHtml(CanvasConverter.toHtmlInline(document))),
      binary,
    )
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromHtml(CanvasConverter.toHtml(document))),
      binary,
    )
    const again: CanvasDocument = CanvasConverter.fromBinary(binary)
    assert.equal(again.name, '演示')
    assert.equal(again.root.children().length, 9)
  }

  /** @anchor canvas-float */
  private static floats(): void {
    for (const value of [0.1, 1 / 3, -0.2, 0, -0]) {
      const text: string = FloatText.format(value)
      assert.equal(
        FloatText.bits(FloatText.parse(text, 'value')),
        FloatText.bits(Math.fround(value)),
      )
    }
    assert.equal(FloatText.format(Math.fround(1 / 3)), '0.33333334')
    for (const invalid of ['+1', ' 1', '1 ', '.1', '1.'])
      assert.throws((): number => FloatText.parse(invalid, 'value'))
    const channel: number = Math.fround(0.1)
    const channelText: string = FloatText.formatChannel(channel)
    assert.equal(FloatText.bits(Math.fround(Number(channelText) / 255)), FloatText.bits(channel))
    const negativeZeroChannel: string = FloatText.formatChannel(-0)
    assert.equal(negativeZeroChannel, '-0')
    assert.equal(FloatText.bits(FloatText.parse(negativeZeroChannel, 'channel')), 0x80000000)
    const named: ColorVar = ColorVar.named('危险')
    assert.equal(Option.isSome(named.palette) ? named.palette.value : '', '危险')
  }

  /** @anchor canvas-html */
  private static html(): void {
    const inline: string = CanvasConverter.toHtmlInline(CanvasConversionTest.sample())
    assert.equal(inline.includes('/>'), false)
    assert.equal(inline.includes('parentanchor="MIDDLE_CENTER"'), true)
    assert.equal(inline.includes('label="按钮\\u201c引号\\u201d"'), true)
    assert.equal(inline.includes('legacypos='), true)
    assert.equal(inline.includes('palette(危险)'), true)
    assert.equal(inline.includes('pngguids="a,b"'), true)
    const closed: string = '<BeCanvas name="empty" width="1080" height="2160" />'
    const normalized: string = CanvasConverter.toHtmlInline(CanvasConverter.fromHtml(closed))
    assert.equal(normalized.includes('></BeCanvas>'), true)
    assert.equal(normalized.includes('/>'), false)
    const shell: string = CanvasConverter.toHtml(CanvasConversionTest.sample())
    const parsed: Document = new DOMParser({ onError: onErrorStopParsing }).parseFromString(
      shell.replace('<!DOCTYPE html>', ''),
      'application/xml',
    )
    assert.equal(parsed.documentElement?.tagName, 'html')
    assert.equal(parsed.getElementsByTagName('script').length, 0)
    assert.equal(parsed.getElementsByTagName('link').length, 0)
    assert.equal(parsed.getElementsByTagName('BeCanvas').item(0)?.getAttribute('width'), '1080')
    assert.deepEqual(
      CanvasConverter.toBinary(CanvasConverter.fromHtml(shell)),
      CanvasConverter.toBinary(CanvasConversionTest.sample()),
    )
  }

  /** @anchor canvas-schema */
  private static closed(): void {
    assert.equal(CanvasSchema.supported('UIRect'), true)
    assert.equal(CanvasSchema.supported('Missing'), false)
    assert.equal(CanvasSchema.accepts('UIRect', 'name', 6), true)
    assert.equal(CanvasSchema.accepts('UIRect', 'type', 6), false)
    assert.equal(CanvasSchema.accepts('UIButton', 'label', 6), true)
    assert.equal(CanvasSchema.kind('pngGuids'), 'list')
    assert.equal(CanvasSchema.kind('color'), 'color')
    assert.throws((): CanvasDocument =>
      CanvasConverter.fromHtml(
        '<BeCanvas name="x" width="1080" height="2160" extra="1"></BeCanvas>',
      ),
    )
    assert.throws((): CanvasDocument =>
      CanvasConverter.fromHtml(
        '<BeCanvas name="x" width="1080" height="2160"><BeUIRect name="a" parentanchor="middle_center"></BeUIRect></BeCanvas>',
      ),
    )
  }

  /** @anchor canvas-binary */
  private static rejects(): void {
    assert.throws((): CanvasDocument => CanvasConverter.fromBinary(new Uint8Array()))
    assert.throws((): CanvasDocument => CanvasConverter.fromInline('canvas:name'))
    assert.throws((): CanvasDocument => CanvasConverter.fromInline('canvas:name=****'))
    assert.throws((): CanvasDocument => CanvasConverter.fromInline('==hjson:name:\n{}=='))
    assert.throws((): CanvasDocument => CanvasConverter.fromInline('not-canvas'))
    assert.throws((): CanvasDocument => CanvasConverter.fromHtml('<html></html>'))
    assert.throws((): Uint8Array =>
      CanvasConverter.toBinary(CanvasDocument.of('x', CanvasNode.create('UIRect'))),
    )
    const emptyPalette: CanvasDocument = CanvasConversionTest.emptyPalette()
    assert.throws((): Uint8Array => CanvasConverter.toBinary(emptyPalette), /调色板名称不能为空/)
    assert.throws((): string => CanvasConverter.toInlineBase64(emptyPalette), /调色板名称不能为空/)
    assert.throws((): string => CanvasConverter.toInlineHjson(emptyPalette), /调色板名称不能为空/)
    assert.throws((): string => CanvasConverter.toHtmlInline(emptyPalette), /调色板名称不能为空/)
    assert.throws(
      (): CanvasDocument =>
        CanvasConverter.fromHtml(
          '<BeCanvas name="n" width="1080" height="2160"><BeUIRect name="UIRect" color="palette()"></BeUIRect></BeCanvas>',
        ),
      /调色板名称不能为空/,
    )
    assert.throws(
      (): CanvasDocument =>
        CanvasConverter.fromInline('==hjson:n:\n[{"type":"UIRect","name":"UIRect","color":""}]=='),
      /调色板名称不能为空/,
    )
    assert.throws(
      (): CanvasDocument =>
        CanvasConverter.fromBinary(
          Buffer.from(
            '027631016e10010011030004747970650606554952656374046e616d65060655495265637405636f6c6f720600',
            'hex',
          ),
        ),
      /调色板名称不能为空/,
    )
    assert.throws(
      (): CanvasDocument =>
        CanvasConverter.fromBinary(
          Buffer.from(
            '027631016e10010011030004747970650606554952656374046e616d65060655495265637405636f6c6f720c000080bf00',
            'hex',
          ),
        ),
      /调色板名称不能为空/,
    )
  }

  /** @anchor canvas-schema */
  private static htmlSchemaMatrix(): void {
    const types: readonly string[] = [
      'UIRect',
      'UIButton',
      'UILabel',
      'TextPro',
      'UIInput',
      'UIRawImage',
      'UISpine',
      'ScrollRect',
    ]
    for (const typeName of types) {
      const tag: HtmlTag = Option.getOrThrow(CanvasTags.byType(typeName))
      for (const attribute of tag.attributes) {
        if (attribute.kind !== HtmlKind.field) continue
        const kind: FieldKind = CanvasSchema.kind(attribute.fieldName)
        const typeCode: number = CanvasConversionTest.typeCode(kind)
        assert.equal(
          CanvasSchema.accepts(typeName, attribute.fieldName, typeCode),
          true,
          `${typeName}.${attribute.fieldName}`,
        )
      }
    }
  }

  private static typeCode(kind: FieldKind): number {
    if (kind === 'bool') return 5
    if (kind === 'float') return 1
    if (kind === 'long') return 24
    if (kind === 'vector') return 4
    if (kind === 'color') return 12
    if (kind === 'list') return 16
    return 6
  }

  private static emptyPalette(): CanvasDocument {
    const root: CanvasNode = CanvasNode.create('Canvas')
    root.addChild(
      CanvasConversionTest.named('UIRect', [
        CanvasConversionTest.pair('color', ColorVar.named('')),
      ]),
    )
    return CanvasDocument.of('n', root)
  }

  private static sample(): CanvasDocument {
    const root: CanvasNode = CanvasNode.create('Canvas')
    root.addChild(CanvasConversionTest.rect())
    root.addChild(CanvasConversionTest.button())
    root.addChild(
      CanvasConversionTest.named('UILabel', [
        CanvasConversionTest.pair('text', StringVar.of('甲&乙')),
      ]),
    )
    root.addChild(
      CanvasConversionTest.named('TextPro', [
        CanvasConversionTest.pair('bUNDERLAY', BoolVar.of(false)),
      ]),
    )
    root.addChild(
      CanvasConversionTest.named('UIInput', [
        CanvasConversionTest.pair('contentType', LongVar.of(1n)),
      ]),
    )
    root.addChild(
      CanvasConversionTest.named('UIRawImage', [
        CanvasConversionTest.pair('fileGuid', StringVar.of('file')),
      ]),
    )
    root.addChild(
      CanvasConversionTest.named('UISpine', [
        CanvasConversionTest.pair('pngGuids', ListVar.of([StringVar.of('a'), StringVar.of('b')])),
      ]),
    )
    root.addChild(
      CanvasConversionTest.named('ScrollRect', [
        CanvasConversionTest.pair('scrollVertical', BoolVar.of(true)),
      ]),
    )
    const nested: CanvasNode = CanvasConversionTest.rect()
    nested.addChild(CanvasConversionTest.named('UILabel', []))
    root.addChild(nested)
    return CanvasDocument.of('演示', root)
  }

  private static rect(): CanvasNode {
    return CanvasConversionTest.named('UIRect', [
      CanvasConversionTest.pair('Pos', Vector3Var.of(0.1, -0.2, 0)),
      CanvasConversionTest.pair('pos', Vector3Var.of(1, 2, 3)),
      CanvasConversionTest.pair('size', Vector3Var.of(10, 20, 1)),
      CanvasConversionTest.pair('color', ColorVar.rgba(0.1, 0.2, 0.3, 1)),
      CanvasConversionTest.pair('mask', BoolVar.of(true)),
      CanvasConversionTest.pair('parentAnchor', LongVar.of(4n)),
      CanvasConversionTest.pair('rot', FloatVar.of(1 / 3)),
      CanvasConversionTest.pair('shadowColor', ColorVar.named('危险')),
    ])
  }

  private static button(): CanvasNode {
    return CanvasConversionTest.named('UIButton', [
      CanvasConversionTest.pair('label', StringVar.of('按钮“引号”')),
      CanvasConversionTest.pair('FontSize', FloatVar.of(18)),
    ])
  }

  private static named(typeName: string, fields: readonly (readonly [string, Var])[]): CanvasNode {
    const node: CanvasNode = CanvasNode.create(typeName)
    node.setField('type', StringVar.of(typeName))
    node.setField('name', StringVar.of(typeName))
    for (const field of fields) node.setField(field[0], field[1])
    return node
  }

  private static pair<T>(name: string, value: T): readonly [string, T] {
    return [name, value]
  }
}
