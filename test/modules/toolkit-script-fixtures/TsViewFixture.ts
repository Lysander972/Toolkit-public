import { Option } from 'effect'
import type { DeviceConversionBasis } from '../../../src/toolkit/script/ts_conversion/DeviceTsConverter.ts'
import { BescriptConverter } from '../../../src/toolkit/script/conversion/BescriptConverter.ts'
import type {
  BescriptDeviceContext,
  DeviceFunction,
} from '../../../src/toolkit/script/device_context/DeviceContext.ts'

/** @anchor bescript-ts-cache */
export class TsViewFixture {
  private constructor() {}

  /** @anchor bescript-ts-cache */
  public static basis(): DeviceConversionBasis {
    return {
      identity: {
        deviceRelativePath: '机械/测试.mech_/0',
        id: '0',
        scope: 'mech',
        namespaceName: 'Mech_Test',
        className: 'Device_Test',
        baseClassName: 'DeviceType_54',
        internalCore: Option.none(),
      },
      scripts: ['first', 'second'].map(
        (name: string): DeviceConversionBasis['scripts'][number] => ({
          kind: 'custom',
          eventName: Option.none(),
          bindingName: name,
          scriptRelativePath: '机械/测试.mech_/0/scripts/' + name + '.code',
          guid: Option.none(),
          tsName: name,
        }),
      ),
      targets: [],
      types: [
        { name: 'String', tsName: 'BeString', bases: [] },
        { name: 'Bool', tsName: 'BeBool', bases: [] },
      ],
      methods: [
        {
          kind: 'fun',
          ownerType: Option.none(),
          besName: 'echo',
          tsName: 'echo',
          receiver: 'G',
          typeArguments: [],
          parameters: [
            { typeName: 'String', valueType: 'String', constant: 'value', enumName: Option.none() },
          ],
          argumentOrder: [0],
          returnType: Option.none(),
          asynchronous: false,
        },
        {
          kind: 'fun',
          ownerType: Option.none(),
          besName: 'predicate',
          tsName: 'predicate',
          receiver: 'G',
          typeArguments: [],
          parameters: [],
          argumentOrder: [],
          returnType: Option.some('Bool'),
          asynchronous: false,
        },
      ],
      events: [],
      enums: [],
      identifiers: [],
    }
  }

  /** @anchor bescript-ts-cache */
  public static context(value: string): BescriptDeviceContext {
    return {
      metadata: {
        deviceType: 54,
        logicName: Option.none(),
        mechFileGuid: Option.none(),
        stickToBone: Option.none(),
      },
      globalVariables: [],
      scriptGroups: [
        {
          kind: 'custom',
          eventName: Option.none(),
          entries: ['first', 'second'].map((name: string): DeviceFunction => ({
            binding: { origin: 'inline', name, guid: Option.none(), isPrivate: false },
            ast: BescriptConverter.parse(
              '\nbegincodeV2\nname:' +
                name +
                '\nlines:\n12:fun echo(String ' +
                (name === 'first' ? value : '固定') +
                ')\n/*if{fun predicate()\n  fun echo(String 启用)\n/*}\n// 注释  \nendcode\n\n',
            ),
          })),
        },
      ],
    }
  }
}
