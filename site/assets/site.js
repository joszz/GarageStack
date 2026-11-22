// The Copy button on the install command. It stays hidden where the browser offers no
// clipboard (a page opened over plain HTTP), so it never shows a button that does nothing.
const COPIED_FOR_MS = 2000

for (const button of document.querySelectorAll('[data-copy-target]')) {
  const target = document.getElementById(button.dataset.copyTarget)
  const label = button.querySelector('.copy-label')
  if (!target || !label || !navigator.clipboard) continue

  const idleText = label.textContent
  let timer = 0
  button.hidden = false
  button.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(target.textContent)
      label.textContent = 'Copied'
      button.classList.add('is-copied')
    } catch {
      label.textContent = 'Select and copy by hand'
    }
    clearTimeout(timer)
    timer = setTimeout(() => {
      label.textContent = idleText
      button.classList.remove('is-copied')
    }, COPIED_FOR_MS)
  })
}
