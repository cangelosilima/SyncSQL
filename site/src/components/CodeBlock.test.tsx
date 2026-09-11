import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import CodeBlock from './CodeBlock'

describe('SQL definition display', () => {
  it('formats clauses while preserving quoted content and restores the exact original', () => {
    const code = "  select [odd name], 'a;  b''c' from [dbo].[Orders] where id = 1;\n\n"
    const { container, rerender } = render(<CodeBlock code={code} />)
    expect(container.querySelector('code')?.textContent).toBe(code)
    rerender(<CodeBlock code={code} formatted />)
    const formatted = container.querySelector('code')?.textContent ?? ''
    expect(formatted).toContain("'a;  b''c'")
    expect(formatted).toMatch(/select\n\s+\[odd name\]/)
    expect(formatted).toMatch(/\nfrom\n/)
    expect(container.querySelector('pre')).toHaveClass('code-block--formatted')
    rerender(<CodeBlock code={code} />)
    expect(container.querySelector('code')?.textContent).toBe(code)
    expect(container.querySelector('pre')).not.toHaveClass('code-block--formatted')
  })

  it('separates EXEC statements without rewriting dynamic SQL strings or GO batches', () => {
    const literal = "N'SELECT * FROM OPENQUERY(HELIOS_ORACLE, ''SELECT ID, AMOUNT FROM PROCUREMENT.V_ITEMS'');'"
    const code = `SET ANSI_NULLS ON;\nGO\nCREATE PROCEDURE ORDER_ENTRY.P_TWO AS EXEC(${literal}); EXEC(${literal});`
    const { container } = render(<CodeBlock code={code} formatted />)
    const formatted = container.querySelector('code')?.textContent ?? ''
    expect(formatted.split(literal)).toHaveLength(3)
    expect(formatted).toContain('\nGO\n')
    expect(formatted).toMatch(/;\n\nEXEC/)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('preserves Oracle alternative quoting and comments', () => {
    const code = "CREATE OR REPLACE VIEW v AS SELECT q'[it's  unchanged;]' AS text FROM dual; -- keep this"
    const { container } = render(<CodeBlock code={code} formatted />)
    expect(container.querySelector('code')?.textContent).toContain("q'[it's  unchanged;]'")
    expect(container.querySelector('code')?.textContent).toContain('-- keep this')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('keeps unsupported SQL visible and escapes markup', () => {
    const code = "SELECT '<script>unterminated"
    const { container } = render(<CodeBlock code={code} formatted />)
    expect(container.querySelector('code')?.textContent).toBe(code)
    expect(container.querySelector('script')).toBeNull()
    expect(screen.getByRole('status')).toHaveTextContent('could not be formatted')
  })

  it('keeps the empty definition state in formatted mode', () => {
    render(<CodeBlock code="" formatted />)
    expect(screen.getByText('No definition captured.')).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})
