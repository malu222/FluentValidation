#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion

namespace FluentValidation.Internal {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Linq.Expressions;
	using System.Reflection;
	using System.Threading;
	using System.Threading.Tasks;
	using Results;
	using Validators;

	/// <summary>
	/// Defines a rule associated with a property.
	/// </summary>
	public class PropertyRule<T, TProperty> : IValidationRule<T, TProperty> {
		private readonly List<IPropertyValidator> _validators = new List<IPropertyValidator>();
		private Func<CascadeMode> _cascadeModeThunk;
		private string _propertyDisplayName;
		private string _propertyName;
		private string[] _ruleSet = new string[0];
		private Func<IValidationContext<T>, bool> _condition;
		private Func<IValidationContext<T>, CancellationToken, Task<bool>> _asyncCondition;
		private string _displayName;
		private Func<IValidationContext<T>, string> _displayNameFactory;

		internal Func<IValidationContext<T>, IEnumerable<ValidationFailure>> ValidationFunction { get; set; }
		internal Func<IValidationContext<T>, CancellationToken, Task<IEnumerable<ValidationFailure>>> AsyncValidationFunction { get; set; }

		/// <summary>
		/// Condition for all validators in this rule.
		/// </summary>
		public Func<IValidationContext<T>, bool> Condition => _condition;

		/// <summary>
		/// Asynchronous condition for all validators in this rule.
		/// </summary>
		public Func<IValidationContext<T>, CancellationToken, Task<bool>> AsyncCondition => _asyncCondition;

		/// <summary>
		/// Property associated with this rule.
		/// </summary>
		public MemberInfo Member { get; }

		/// <summary>
		/// Function that can be invoked to retrieve the value of the property.
		/// </summary>
		public Func<T, TProperty> PropertyFunc { get; }

		/// <summary>
		/// Expression that was used to create the rule.
		/// </summary>
		public LambdaExpression Expression { get; }

		/// <summary>
		/// Sets the display name for the property.
		/// </summary>
		/// <param name="name">The property's display name</param>
		public void SetDisplayName(string name) {
			_displayName = name;
			_displayNameFactory = null;
		}

		/// <summary>
		/// Sets the display name for the property using a function.
		/// </summary>
		/// <param name="factory">The function for building the display name</param>
		public void SetDisplayName(Func<IValidationContext<T>, string> factory) {
			if (factory == null) throw new ArgumentNullException(nameof(factory));
			_displayNameFactory = factory;
			_displayName = null;
		}

		/// <summary>
		/// Rule set that this rule belongs to (if specified)
		/// </summary>
		public string[] RuleSets {
			get => _ruleSet;
			set => _ruleSet = value ?? new string[0];
		}

		/// <summary>
		/// Function that will be invoked if any of the validators associated with this rule fail.
		/// </summary>
		public Action<T, IEnumerable<ValidationFailure>> OnFailure { get; set; }

		/// <summary>
		/// The current validator being configured by this rule.
		/// </summary>
		public IPropertyValidator CurrentValidator => _validators.LastOrDefault();

		/// <summary>
		/// Type of the property being validated
		/// </summary>
		public virtual Type TypeToValidate => typeof(TProperty);

		/// <summary>
		/// Cascade mode for this rule.
		/// </summary>
		public CascadeMode CascadeMode {
			get => _cascadeModeThunk();
			set => _cascadeModeThunk = () => value;
		}

		/// <summary>
		/// Validators associated with this rule.
		/// </summary>
		public IEnumerable<IPropertyValidator> Validators => _validators;

		/// <summary>
		/// Creates a new property rule.
		/// </summary>
		/// <param name="member">Property</param>
		/// <param name="propertyFunc">Function to get the property value</param>
		/// <param name="expression">Lambda expression used to create the rule</param>
		/// <param name="cascadeModeThunk">Function to get the cascade mode.</param>
		public PropertyRule(MemberInfo member, Func<T, TProperty> propertyFunc, LambdaExpression expression, Func<CascadeMode> cascadeModeThunk) {
			Member = member;
			PropertyFunc = propertyFunc;
			Expression = expression;
			_cascadeModeThunk = cascadeModeThunk;

			DependentRules = new List<IValidationRule<T>>();
			PropertyName = ValidatorOptions.Global.PropertyNameResolver(typeof(T), member, expression);
			_displayNameFactory = context => ValidatorOptions.Global.DisplayNameResolver(typeof(T), member, expression);
		}

		/// <summary>
		/// Creates a new property rule from a lambda expression.
		/// </summary>
		public static PropertyRule<T, TProperty> Create(Expression<Func<T, TProperty>> expression) {
			return Create(expression, () => ValidatorOptions.Global.CascadeMode);
		}

		/// <summary>
		/// Creates a new property rule from a lambda expression.
		/// </summary>
		public static PropertyRule<T, TProperty> Create(Expression<Func<T, TProperty>> expression, Func<CascadeMode> cascadeModeThunk, bool bypassCache = false) {
			var member = expression.GetMember();
			var compiled = AccessorCache<T>.GetCachedAccessor(member, expression, bypassCache);
			var rule = new PropertyRule<T, TProperty>(member, compiled, expression, cascadeModeThunk);
			return rule;
		}

		/// <summary>
		/// Adds a validator to the rule.
		/// </summary>
		public void AddValidator(IPropertyValidator validator) {
			_validators.Add(validator);
		}

		/// <summary>
		/// Replaces a validator in this rule. Used to wrap validators.
		/// </summary>
		public void ReplaceValidator(IPropertyValidator original, IPropertyValidator newValidator) {
			var index = _validators.IndexOf(original);

			if (index > -1) {
				_validators[index] = newValidator;
			}
		}

		/// <summary>
		/// Remove a validator in this rule.
		/// </summary>
		public void RemoveValidator(IPropertyValidator original) {
			_validators.Remove(original);
		}

		/// <summary>
		/// Clear all validators from this rule.
		/// </summary>
		public void ClearValidators() {
			_validators.Clear();
		}

		/// <summary>
		/// Returns the property name for the property being validated.
		/// Returns null if it is not a property being validated (eg a method call)
		/// </summary>
		public string PropertyName {
			get { return _propertyName; }
			set {
				_propertyName = value;
				_propertyDisplayName = _propertyName.SplitPascalCase();
			}
		}

		/// <inheritdoc />
		public bool HasCondition => _condition != null;

		/// <inheritdoc />
		public bool HasAsyncCondition => _asyncCondition != null;

		/// <summary>
		/// Allows custom creation of an error message
		/// </summary>
		public Func<MessageBuilderContext, string> MessageBuilder { get; set; }

		/// <summary>
		/// Dependent rules
		/// </summary>
		public List<IValidationRule<T>> DependentRules { get; }

		string IValidationRule.GetDisplayName(IValidationContext context)
			=> GetDisplayName(ValidationContext<T>.GetFromNonGenericContext(context));

		/// <summary>
		/// Display name for the property.
		/// </summary>
		public string GetDisplayName(IValidationContext<T> context)
			=> _displayNameFactory?.Invoke(context) ?? _displayName ?? _propertyDisplayName;

		/// <summary>
		/// Performs validation using a validation context and returns a collection of Validation Failures.
		/// </summary>
		/// <param name="context">Validation Context</param>
		/// <returns>A collection of validation failures</returns>
		public IEnumerable<ValidationFailure> Validate(IValidationContext<T> context) {
			if (ValidationFunction != null) {
				return ValidationFunction(context);
			}

			return ValidateInternal(context, PropertyFunc);
		}

		/// <summary>
		/// Performs asynchronous validation using a validation context and returns a collection of Validation Failures.
		/// </summary>
		/// <param name="context">Validation Context</param>
		/// <param name="cancellation"></param>
		/// <returns>A collection of validation failures</returns>
		public Task<IEnumerable<ValidationFailure>> ValidateAsync(IValidationContext<T> context, CancellationToken cancellation) {
			if (AsyncValidationFunction != null) {
				return AsyncValidationFunction(context, cancellation);
			}

			return ValidateInternalAsync(context, PropertyFunc, cancellation);
		}

		internal virtual IEnumerable<ValidationFailure> ValidateInternal<TValue>(IValidationContext<T> context, Func<T, TValue> accessorFunc) {
			string displayName = GetDisplayName(context);

			if (PropertyName == null && displayName == null) {
				//No name has been specified. Assume this is a model-level rule, so we should use empty string instead.
				displayName = string.Empty;
			}

			// Construct the full name of the property, taking into account overriden property names and the chain (if we're in a nested validator)
			string propertyName = context.PropertyChain.BuildPropertyName(PropertyName ?? displayName);

			// Ensure that this rule is allowed to run.
			// The validatselector has the opportunity to veto this before any of the validators execute.
			if (!context.Selector.CanExecute(this, propertyName, context)) {
				return Enumerable.Empty<ValidationFailure>();
			}

			if (HasCondition) {
				if (!Condition(context)) {
					return Enumerable.Empty<ValidationFailure>();
				}
			}

			// TODO: For FV 9, throw an exception by default if synchronous validator has async condition.
			if (HasAsyncCondition) {
				if (! AsyncCondition(context, default).GetAwaiter().GetResult()) {
					return Enumerable.Empty<ValidationFailure>();
				}
			}

			var cascade = CascadeMode;
			var failures = new List<ValidationFailure>();
			var accessor = new Lazy<TValue>(() => accessorFunc(context.InstanceToValidate), LazyThreadSafetyMode.None);

			// Invoke each validator and collect its results.
			foreach (var validator in Validators) {
				IEnumerable<ValidationFailure> results;
				if (validator.ShouldValidateAsynchronously(context))
					//TODO: For FV 9 by default disallow invocation of async validators when running synchronously.
					results = InvokePropertyValidatorAsync(context, validator, propertyName, accessor, default).GetAwaiter().GetResult();
				else
					results = InvokePropertyValidator(context, validator, propertyName, accessor);

				bool hasFailure = false;

				foreach (var result in results) {
					failures.Add(result);
					hasFailure = true;
				}

				// If there has been at least one failure, and our CascadeMode has been set to StopOnFirst
				// then don't continue to the next rule
#pragma warning disable 618
				if (hasFailure && (cascade == CascadeMode.StopOnFirstFailure || cascade == CascadeMode.Stop)) {
#pragma warning restore 618
					break;
				}
			}

			if (failures.Count > 0) {
				// Callback if there has been at least one property validator failed.
				OnFailure?.Invoke(context.InstanceToValidate, failures);
			}
			else {
				foreach (var dependentRule in DependentRules) {
					failures.AddRange(dependentRule.Validate(context));
				}
			}

			return failures;
		}

		internal virtual async Task<IEnumerable<ValidationFailure>> ValidateInternalAsync<TValue>(IValidationContext<T> context, Func<T, TValue> accessorFunc, CancellationToken cancellation) {
			if (!context.IsAsync()) {
				context.RootContextData["__FV_IsAsyncExecution"] = true;
			}

			string displayName = GetDisplayName(context);

			if (PropertyName == null && displayName == null) {
				//No name has been specified. Assume this is a model-level rule, so we should use empty string instead.
				displayName = string.Empty;
			}

			// Construct the full name of the property, taking into account overriden property names and the chain (if we're in a nested validator)
			string propertyName = context.PropertyChain.BuildPropertyName(PropertyName ?? displayName);

			// Ensure that this rule is allowed to run.
			// The validatselector has the opportunity to veto this before any of the validators execute.
			if (!context.Selector.CanExecute(this, propertyName, context)) {
				return Enumerable.Empty<ValidationFailure>();
			}

			if (HasCondition) {
				if (!Condition(context)) {
					return Enumerable.Empty<ValidationFailure>();
				}
			}

			if (HasAsyncCondition) {
				if (! await AsyncCondition(context, cancellation)) {
					return Enumerable.Empty<ValidationFailure>();
				}
			}

			var cascade = CascadeMode;
			var failures = new List<ValidationFailure>();
			var accessor = new Lazy<TValue>(() => accessorFunc(context.InstanceToValidate), LazyThreadSafetyMode.None);

			// Invoke each validator and collect its results.
			foreach (var validator in Validators) {
				cancellation.ThrowIfCancellationRequested();

				IEnumerable<ValidationFailure> results;
				if (validator.ShouldValidateAsynchronously(context))
					results = await InvokePropertyValidatorAsync(context, validator, propertyName, accessor, cancellation);
				else
					results = InvokePropertyValidator(context, validator, propertyName, accessor);

				bool hasFailure = false;

				foreach (var result in results) {
					failures.Add(result);
					hasFailure = true;
				}

				// If there has been at least one failure, and our CascadeMode has been set to StopOnFirst
				// then don't continue to the next rule
#pragma warning disable 618
				if (hasFailure && (cascade == CascadeMode.StopOnFirstFailure || cascade == CascadeMode.Stop)) {
#pragma warning restore 618
					break;
				}
			}

			if (failures.Count > 0) {
				// Callback if there has been at least one property validator failed.
				OnFailure?.Invoke(context.InstanceToValidate, failures);
			}
			else {
				failures.AddRange(await RunDependentRulesAsync(context, cancellation));
			}

			return failures;
		}

		private async Task<IEnumerable<ValidationFailure>> InvokePropertyValidatorAsync<TValue>(IValidationContext<T> context, IPropertyValidator validator, string propertyName, Lazy<TValue> accessor, CancellationToken cancellation) {
			if (!validator.Options.InvokeCondition(context)) return Enumerable.Empty<ValidationFailure>();
			if (!await validator.Options.InvokeAsyncCondition(context, cancellation)) return Enumerable.Empty<ValidationFailure>();
			var propertyContext = new PropertyValidatorContext(context, this, propertyName, accessor.Value);
			return await validator.ValidateAsync(propertyContext, cancellation);
		}

		private IEnumerable<ValidationFailure> InvokePropertyValidator<TValue>(IValidationContext<T> context, IPropertyValidator validator, string propertyName, Lazy<TValue> accessor) {
			if (!validator.Options.InvokeCondition(context)) return Enumerable.Empty<ValidationFailure>();
			var propertyContext = new PropertyValidatorContext(context, this, propertyName, accessor.Value);
			return validator.Validate(propertyContext);
		}

		internal async Task<IEnumerable<ValidationFailure>> RunDependentRulesAsync(IValidationContext<T> context, CancellationToken cancellation) {
			var failures = new List<ValidationFailure>();

			foreach (var rule in DependentRules) {
				cancellation.ThrowIfCancellationRequested();
				failures.AddRange(await rule.ValidateAsync(context, cancellation));
			}

			return failures;
		}

		/// <summary>
		/// Applies a condition to the rule
		/// </summary>
		/// <param name="predicate"></param>
		/// <param name="applyConditionTo"></param>
		public void ApplyCondition(Func<IValidationContext<T>, bool> predicate, ApplyConditionTo applyConditionTo = ApplyConditionTo.AllValidators) {
			// Default behaviour for When/Unless as of v1.3 is to apply the condition to all previous validators in the chain.
			// TODO: Remove the currying once PropertyValidators are generic.
			if (applyConditionTo == ApplyConditionTo.AllValidators) {
				foreach (var validator in Validators) {
					validator.Options.ApplyCondition(ctx=> predicate(ValidationContext<T>.GetFromNonGenericContext(ctx)));
				}

				foreach (var dependentRule in DependentRules) {
					dependentRule.ApplyCondition(predicate, applyConditionTo);
				}
			}
			else {
				CurrentValidator.Options.ApplyCondition(ctx=> predicate(ValidationContext<T>.GetFromNonGenericContext(ctx)));
			}
		}

		/// <summary>
		/// Applies the condition to the rule asynchronously
		/// </summary>
		/// <param name="predicate"></param>
		/// <param name="applyConditionTo"></param>
		public void ApplyAsyncCondition(Func<IValidationContext<T>, CancellationToken, Task<bool>> predicate, ApplyConditionTo applyConditionTo = ApplyConditionTo.AllValidators) {
			// Default behaviour for When/Unless as of v1.3 is to apply the condition to all previous validators in the chain.
			// TODO: Remove the currying once PropertyValidators are generic.
			if (applyConditionTo == ApplyConditionTo.AllValidators) {
				foreach (var validator in Validators) {
					validator.Options.ApplyAsyncCondition((ctx, c) => predicate(ValidationContext<T>.GetFromNonGenericContext(ctx), c));
				}

				foreach (var dependentRule in DependentRules) {
					dependentRule.ApplyAsyncCondition(predicate, applyConditionTo);
				}
			}
			else {
				CurrentValidator.Options.ApplyAsyncCondition((ctx, c) => predicate(ValidationContext<T>.GetFromNonGenericContext(ctx), c));
			}
		}

		public void ApplySharedCondition(Func<IValidationContext<T>, bool> condition) {
			if (_condition == null) {
				_condition = condition;
			}
			else {
				var original = _condition;
				_condition = ctx => condition(ctx) && original(ctx);
			}
		}

		public void ApplySharedAsyncCondition(Func<IValidationContext<T>, CancellationToken, Task<bool>> condition) {
			if (_asyncCondition == null) {
				_asyncCondition = condition;
			}
			else {
				var original = _asyncCondition;
				_asyncCondition = async (ctx, ct) => await condition(ctx, ct) && await original(ctx, ct);
			}

		}
	}
}
