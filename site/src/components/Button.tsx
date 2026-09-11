import { forwardRef, type ButtonHTMLAttributes } from 'react'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement>

/** Visual action primitive; callers retain their own action and busy-state logic. */
const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  { className = '', type = 'button', ...props },
  ref,
) {
  return <button {...props} ref={ref} type={type} className={`ui-button ${className}`.trim()} />
})

export default Button
