/** Integration tests: run against a deployed stack (reads cdk.outputs/outputs.json). */
module.exports = {
  testEnvironment: 'node',
  roots: ['<rootDir>/integration'],
  testMatch: ['**/*.test.ts'],
  transform: {
    '^.+\\.tsx?$': ['@swc/jest'],
  },
  testTimeout: 120_000,
};
