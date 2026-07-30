'use strict';

// §45: bind test discovery to the SAME authoritative source roots as tsconfig (source-roots.json
// is the single canonical definition). Prevents unbounded discovery into vendor/projection trees.
const authoritativeRoots = require('./source-roots.json').map((r) => '<rootDir>/' + r);

module.exports = {
  testEnvironment: 'node',
  collectCoverage: true,
  coverageDirectory: 'coverage',
  roots: authoritativeRoots,
  testPathIgnorePatterns: [
    '/node_modules/', '/dotnet/', '/WebX/', '/bin/', '/dist/', '/build/',
    '/coverage/', '/.learning/', '/.holotape/', '/.backups/', '/.deepseek/',
  ],
  testMatch: [
    '**/__tests__/**/*.+(ts|tsx|js)',
    '**/*.(test|spec).+(ts|tsx|js)',
  ],
  transform: {
    '^.+\\.(js|ts|tsx)$': 'babel-jest',
  },
  moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json'],
  // Remap .js imports to .ts sources inside kuhul/ (TypeScript ES-module style)
  moduleNameMapper: {
    '^(\\.{1,2}/.*)\\.js$': '$1',
  },
  extensionsToTreatAsEsm: [],
};