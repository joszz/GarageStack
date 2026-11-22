// Gives every code block a header with its label and a Copy button. The button is left out
// where the browser offers no clipboard (a page opened over plain HTTP), so it never shows a
// button that does nothing; the label still shows.
const COPIED_FOR_MS = 2000
const SVG = 'http://www.w3.org/2000/svg'

function icon(name) {
  const svg = document.createElementNS(SVG, 'svg')
  svg.setAttribute('class', 'icon')
  svg.setAttribute('aria-hidden', 'true')
  const use = document.createElementNS(SVG, 'use')
  use.setAttribute('href', `assets/icons.svg#${name}`)
  svg.append(use)
  return svg
}

function copyButton(code) {
  const button = document.createElement('button')
  button.type = 'button'
  button.className = 'btn btn-ghost btn-small copy-button'
  const label = document.createElement('span')
  label.textContent = 'Copy'
  button.append(icon('copy'), label)

  let timer = 0
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(code.textContent)
      label.textContent = 'Copied'
      button.classList.add('is-copied')
    } catch {
      label.textContent = 'Select and copy by hand'
    }
    clearTimeout(timer)
    timer = setTimeout(() => {
      label.textContent = 'Copy'
      button.classList.remove('is-copied')
    }, COPIED_FOR_MS)
  })
  return button
}

for (const block of document.querySelectorAll('.code-block')) {
  const code = block.querySelector('code')
  if (!code) continue

  const head = document.createElement('div')
  head.className = 'code-head'
  const label = document.createElement('span')
  label.className = 'text-muted'
  label.textContent = block.dataset.label ?? ''
  head.append(label)
  if (navigator.clipboard) head.append(copyButton(code))
  block.prepend(head)
}
