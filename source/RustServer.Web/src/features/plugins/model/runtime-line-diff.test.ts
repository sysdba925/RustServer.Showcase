import { describe, expect, it } from 'vitest'

import {
  createRuntimeLineDiff,
  createRuntimeSideBySideLineDiff,
} from './runtime-line-diff'

describe('createRuntimeLineDiff', () => {
  it('keeps shared lines neutral around a modified line', () => {
    expect(createRuntimeLineDiff('first\nold\nlast', 'first\nnew\nlast')).toEqual([
      { kind: 'unchanged', text: 'first' },
      { kind: 'removed', text: 'old' },
      { kind: 'added', text: 'new' },
      { kind: 'unchanged', text: 'last' },
    ])
  })

  it('represents complete added and deleted text files', () => {
    expect(createRuntimeLineDiff('', 'first\nsecond')).toEqual([
      { kind: 'added', text: 'first' },
      { kind: 'added', text: 'second' },
    ])
    expect(createRuntimeLineDiff('first\nsecond', '')).toEqual([
      { kind: 'removed', text: 'first' },
      { kind: 'removed', text: 'second' },
    ])
  })

  it('aligns changed blocks into paired Baseline and Runtime rows', () => {
    expect(createRuntimeSideBySideLineDiff('first\nold\nlast', 'first\nnew\nlast')).toEqual([
      {
        baseline: { kind: 'unchanged', text: 'first' },
        runtime: { kind: 'unchanged', text: 'first' },
      },
      {
        baseline: { kind: 'removed', text: 'old' },
        runtime: { kind: 'added', text: 'new' },
      },
      {
        baseline: { kind: 'unchanged', text: 'last' },
        runtime: { kind: 'unchanged', text: 'last' },
      },
    ])
  })

  it('keeps the missing side blank for wholly added or deleted files', () => {
    expect(createRuntimeSideBySideLineDiff('', 'first')).toEqual([
      { baseline: null, runtime: { kind: 'added', text: 'first' } },
    ])
    expect(createRuntimeSideBySideLineDiff('first', '')).toEqual([
      { baseline: { kind: 'removed', text: 'first' }, runtime: null },
    ])
  })
})
