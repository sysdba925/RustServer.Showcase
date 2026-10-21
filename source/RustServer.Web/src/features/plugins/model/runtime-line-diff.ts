export type RuntimeDiffLineKind = 'added' | 'removed' | 'unchanged'

export interface RuntimeDiffLine {
  kind: RuntimeDiffLineKind
  text: string
}

export interface RuntimeSideBySideDiffLine {
  baseline: RuntimeDiffLine | null
  runtime: RuntimeDiffLine | null
}

interface LineMatch {
  baselineIndex: number
  runtimeIndex: number
}

/**
 * Produces an exact, unified line diff. Hirschberg's LCS algorithm keeps the
 * comparison linear in memory, which matters when a managed text file is large.
 */
export function createRuntimeLineDiff(baseline: string, runtime: string): RuntimeDiffLine[] {
  const baselineLines = splitLines(baseline)
  const runtimeLines = splitLines(runtime)
  const matches: LineMatch[] = []

  collectLineMatches(
    baselineLines,
    0,
    baselineLines.length,
    runtimeLines,
    0,
    runtimeLines.length,
    matches,
  )

  const lines: RuntimeDiffLine[] = []
  let baselineIndex = 0
  let runtimeIndex = 0

  for (const match of matches) {
    while (baselineIndex < match.baselineIndex) {
      lines.push({ kind: 'removed', text: baselineLines[baselineIndex] })
      baselineIndex += 1
    }

    while (runtimeIndex < match.runtimeIndex) {
      lines.push({ kind: 'added', text: runtimeLines[runtimeIndex] })
      runtimeIndex += 1
    }

    lines.push({ kind: 'unchanged', text: baselineLines[baselineIndex] })
    baselineIndex += 1
    runtimeIndex += 1
  }

  while (baselineIndex < baselineLines.length) {
    lines.push({ kind: 'removed', text: baselineLines[baselineIndex] })
    baselineIndex += 1
  }

  while (runtimeIndex < runtimeLines.length) {
    lines.push({ kind: 'added', text: runtimeLines[runtimeIndex] })
    runtimeIndex += 1
  }

  return lines
}

/**
 * Pairs the unified LCS diff into rows for a two-column comparison. A removed
 * line and a following added line occupy one visual row, while unmatched
 * additions or removals receive an intentionally blank opposite side.
 */
export function createRuntimeSideBySideLineDiff(
  baseline: string,
  runtime: string,
): RuntimeSideBySideDiffLine[] {
  const rows: RuntimeSideBySideDiffLine[] = []
  const removed: RuntimeDiffLine[] = []
  const added: RuntimeDiffLine[] = []

  function appendChangedRows(): void {
    const count = Math.max(removed.length, added.length)
    for (let index = 0; index < count; index += 1) {
      rows.push({
        baseline: removed[index] ?? null,
        runtime: added[index] ?? null,
      })
    }
    removed.length = 0
    added.length = 0
  }

  for (const line of createRuntimeLineDiff(baseline, runtime)) {
    if (line.kind === 'unchanged') {
      appendChangedRows()
      rows.push({ baseline: line, runtime: line })
    } else if (line.kind === 'removed') {
      removed.push(line)
    } else {
      added.push(line)
    }
  }

  appendChangedRows()
  return rows
}

function splitLines(text: string): string[] {
  return text === '' ? [] : text.split(/\r?\n/)
}

function collectLineMatches(
  baseline: readonly string[],
  baselineStart: number,
  baselineEnd: number,
  runtime: readonly string[],
  runtimeStart: number,
  runtimeEnd: number,
  matches: LineMatch[],
): void {
  const baselineLength = baselineEnd - baselineStart
  const runtimeLength = runtimeEnd - runtimeStart

  if (baselineLength === 0 || runtimeLength === 0) {
    return
  }

  if (baselineLength === 1) {
    const baselineLine = baseline[baselineStart]
    for (let runtimeIndex = runtimeStart; runtimeIndex < runtimeEnd; runtimeIndex += 1) {
      if (baselineLine === runtime[runtimeIndex]) {
        matches.push({ baselineIndex: baselineStart, runtimeIndex })
        return
      }
    }

    return
  }

  const baselineMiddle = baselineStart + Math.floor(baselineLength / 2)
  const leftLengths = calculateLcsLengths(
    baseline,
    baselineStart,
    baselineMiddle,
    runtime,
    runtimeStart,
    runtimeEnd,
  )
  const rightLengths = calculateLcsLengths(
    baseline,
    baselineMiddle,
    baselineEnd,
    runtime,
    runtimeStart,
    runtimeEnd,
    true,
    true,
  )

  let runtimeSplitOffset = 0
  let longestCommonSubsequence = -1
  for (let offset = 0; offset <= runtimeLength; offset += 1) {
    const length = leftLengths[offset] + rightLengths[runtimeLength - offset]
    if (length > longestCommonSubsequence) {
      longestCommonSubsequence = length
      runtimeSplitOffset = offset
    }
  }

  const runtimeMiddle = runtimeStart + runtimeSplitOffset
  collectLineMatches(
    baseline,
    baselineStart,
    baselineMiddle,
    runtime,
    runtimeStart,
    runtimeMiddle,
    matches,
  )
  collectLineMatches(
    baseline,
    baselineMiddle,
    baselineEnd,
    runtime,
    runtimeMiddle,
    runtimeEnd,
    matches,
  )
}

function calculateLcsLengths(
  baseline: readonly string[],
  baselineStart: number,
  baselineEnd: number,
  runtime: readonly string[],
  runtimeStart: number,
  runtimeEnd: number,
  reverseBaseline = false,
  reverseRuntime = false,
): Uint32Array {
  const runtimeLength = runtimeEnd - runtimeStart
  let previous = new Uint32Array(runtimeLength + 1)
  let current = new Uint32Array(runtimeLength + 1)

  for (let baselineOffset = 0; baselineOffset < baselineEnd - baselineStart; baselineOffset += 1) {
    const baselineIndex = reverseBaseline
      ? baselineEnd - baselineOffset - 1
      : baselineStart + baselineOffset

    for (let runtimeOffset = 0; runtimeOffset < runtimeLength; runtimeOffset += 1) {
      const runtimeIndex = reverseRuntime
        ? runtimeEnd - runtimeOffset - 1
        : runtimeStart + runtimeOffset
      current[runtimeOffset + 1] = baseline[baselineIndex] === runtime[runtimeIndex]
        ? previous[runtimeOffset] + 1
        : Math.max(previous[runtimeOffset + 1], current[runtimeOffset])
    }

    [previous, current] = [current, previous]
  }

  return previous
}
