"""Jobs that run once per Godot line keep the line in every cache key, sticky-disk key and artifact name, as precision
does: otherwise the lines restore, commit and overwrite each other's state (sticky disks race on commit)."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SHARED = re.compile(r'uses:\s*(?:actions/cache|actions/upload-artifact|actions/download-artifact|useblacksmith/stickydisk)\b')
FIELD = re.compile(r'(key|restore-keys|name):\s*(.*)$')
LINE_AXIS = re.compile(r'^\s+(?:-\s+)?line:', re.M)  # a matrix axis or a workflow input named `line`
LINE_REF = re.compile(r'\b(?:matrix|inputs)\.line\b')


def indent(text):
    return len(text) - len(text.lstrip())


def jobs(workflow):
    """The text before `jobs:`, and each job's lines."""
    lines = workflow.splitlines()
    start = next(i for i, text in enumerate(lines) if text.rstrip() == 'jobs:')
    blocks, current = {}, None
    for text in lines[start + 1:]:
        if match := re.match(r'^  ([\w-]+):\s*$', text):
            current = blocks.setdefault(match.group(1), [])
        elif current is not None:
            current.append(text)
    return '\n'.join(lines[:start]), blocks


def shared_values(body):
    """key/restore-keys/name values (block scalars line by line) under each cache, sticky-disk or artifact step."""
    for i, text in enumerate(body):
        if not SHARED.search(text):
            continue
        column = text.index('uses:')
        field = None
        for inner in body[i + 1:]:
            if not inner.strip():
                continue
            if indent(inner) < column:
                break
            if indent(inner) == column + 2:
                match = FIELD.match(inner.strip())
                field = match and match.group(1)
                if match and match.group(2).strip() not in ('', '|', '>'):
                    yield match.group(2).strip()
            elif field and indent(inner) > column + 2:
                yield inner.strip()


def violations(name, workflow):
    header, blocks = jobs(workflow)
    every_job = bool(LINE_AXIS.search(header))  # a workflow called or dispatched for one line
    for job, body in blocks.items():
        if every_job or LINE_AXIS.search('\n'.join(body)):
            yield from (f'{name}: {job}: {value}' for value in shared_values(body) if not LINE_REF.search(value))


STEP = '''
    steps:
      - name: Cache
        uses: actions/cache@v6
        with:
          path: x
          key: build-${{ matrix.precision }}-%s
          restore-keys: |
            build-${{ matrix.precision }}-%s
      - uses: actions/upload-artifact@v7
        with:
          name: packages-${{ matrix.precision }}-%s
'''


def workflow(header='', axis=True, refs=('${{ matrix.line }}',) * 3):
    matrix = '    strategy:\n      matrix:\n        line: [\'4.7\', \'4.8\']\n' if axis else ''
    return f'name: ci\n{header}jobs:\n  pack:\n{matrix}{STEP % refs}'


class LineKeyTests(unittest.TestCase):
    def test_workflows_keep_lines_apart(self):
        found = [v for path in sorted((ROOT / '.github/workflows').glob('*.yml'))
                 for v in violations(path.name, path.read_text(encoding='utf-8'))]
        self.assertEqual(found, [], 'add ${{ matrix.line }} (or ${{ inputs.line }}) to these keys and names')

    def test_keys_naming_the_line_pass(self):
        self.assertEqual(list(violations('ci.yml', workflow())), [])

    def test_each_missing_line_is_reported(self):
        found = list(violations('ci.yml', workflow(refs=('', '${{ matrix.line }}', ''))))
        self.assertEqual(found, ['ci.yml: pack: build-${{ matrix.precision }}-',
                                 'ci.yml: pack: packages-${{ matrix.precision }}-'])

    def test_restore_key_block_lines_are_checked(self):
        found = list(violations('ci.yml', workflow(refs=('${{ matrix.line }}', '', '${{ matrix.line }}'))))
        self.assertEqual(found, ['ci.yml: pack: build-${{ matrix.precision }}-'])

    def test_jobs_without_a_line_axis_are_ignored(self):
        self.assertEqual(list(violations('ci.yml', workflow(axis=False, refs=('', '', '')))), [])

    def test_a_line_input_covers_every_job(self):
        header = 'on:\n  workflow_call:\n    inputs:\n      line:\n        type: string\n'
        self.assertEqual(len(list(violations('natives.yml', workflow(header, axis=False, refs=('', '', ''))))), 3)


if __name__ == '__main__':
    unittest.main()
