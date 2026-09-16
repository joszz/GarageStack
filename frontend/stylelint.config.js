/**
 * Stylelint keeps the hand-written CSS honest: duplicate selectors, dead overrides and invalid
 * values are the kind of thing that only shows up once a rule silently stops applying.
 * Formatting is Prettier's job, so this config carries correctness and convention rules only.
 */
export default {
  extends: ['stylelint-config-standard'],
  overrides: [
    {
      files: ['**/*.vue'],
      customSyntax: 'postcss-html',
    },
  ],
  rules: {
    // The stylesheets are organized by feature, so a component's own block is allowed to reuse
    // the class names its markup uses; what matters is that a selector is not written twice in
    // the same context.
    'no-descending-specificity': null,
    // BEM-style names with element/modifier separators, and data-theme attribute selectors.
    'selector-class-pattern': null,
    'custom-property-pattern': null,
    'keyframes-name-pattern': null,
    // Vue's scoped-style selectors, which the CSS parser has no way to know about.
    'selector-pseudo-class-no-unknown': [true, { ignorePseudoClasses: ['deep', 'slotted', 'global'] }],
  },
}
