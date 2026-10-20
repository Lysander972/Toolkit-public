/**
 * 核心骨架冒烟：真实引导 NestJS 应用上下文（无替身，容器装配链路必须真跑）。
 */
import 'reflect-metadata';
import { describe, expect, it } from 'vitest';
import { CoreBootstrap } from '../main';

describe('核心骨架', () => {
  it('应用上下文可引导并正常关闭', async () => {
    const context = await CoreBootstrap.create();
    expect(context).toBeDefined();
    await context.close();
  });
});
