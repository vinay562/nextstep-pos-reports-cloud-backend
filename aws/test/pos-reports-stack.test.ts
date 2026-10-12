import * as cdk from 'aws-cdk-lib/core';
import { Template } from 'aws-cdk-lib/assertions';
import { PosReportsStack } from '../lib/pos-reports-stack';

describe('PosReportsStack', () => {
  test('synthesizes', () => {
    const app = new cdk.App();
    const stack = new PosReportsStack(app, 'TestStack');

    expect(() => Template.fromStack(stack)).not.toThrow();
  });
});
