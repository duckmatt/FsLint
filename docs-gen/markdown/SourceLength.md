# SourceLength

Set of rules that analyse the length of sections of code.

### Rules

#### MaxLinesInFunction - FS0025

##### Cause

A function is made up of more than a configurable number of lines.

##### Rationale

The larger a function becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Refactor to extract out code into smaller composable functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a function. (Default 70)

#### MaxLinesInLambdaFunction - FS0022

##### Cause

A lambda function is made up of more than a configurable number of lines.

##### Rationale

Lambda functions are usually used for single lines of code that aren't worth naming to make code more concise. A large lambda function indicates it should probably be a named function.

##### How To Fix

Consider using a named function rather than a lambda function.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a lambda function. (Default 7)

#### MaxLinesInMatchLambdaFunction - FS0023

##### Cause

A match function is made up of more than a configurable number of lines.

##### Rationale

The larger a function becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Use active patterns to help reduce the number of matches/extract code out into composable functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a match function. (Default 70)

#### MaxLinesInValue - FS0024

##### Cause

A statement binded to a value is made up of more than a configurable number of lines. 
For example the following would break the rule when the maximum number of lines is set to 4:

    let value = 
		let x = 7
		let y = 6
		let e = 5
		let r = 4
		r * y * e * x

##### Rationale

The larger a value becomes the more complex it becomes.

##### How To Fix

Refactor to extract out code into smaller composable functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a value binding. (Default 70)

#### MaxLinesInConstructor - FS0027

##### Cause

A class constructor is made up of more than a configurable number of lines.

##### Rationale

The larger a constructor becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Extract code out into private methods or functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a class constructor. (Default 70)

#### MaxLinesInMember - FS0026

##### Cause

A member is made up of more than a configurable number of lines.

##### Rationale

The larger a member becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Extract code out into private methods or functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a member. (Default 70)

#### MaxLinesInProperty - FS0028

##### Cause

A property is made up of more than a configurable number of lines.

##### Rationale

The larger a property becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Extract code out into private methods or functions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a property. (Default 70)

#### MaxLinesInClass - FS0033

##### Cause

A class is made up of more than a configurable number of lines.

##### Rationale

The larger a class becomes the more complex it becomes, it also indicates that it may have [too many different responsibilities](http://en.wikipedia.org/wiki/Single_responsibility_principle).

##### How To Fix

Extract code out into smaller composable classes.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a class. (Default 500)

#### MaxLinesInEnum - FS0031

##### Cause

An enum is made up of more than a configurable number of lines.

##### Rationale

The larger a enum becomes the more complex it becomes, it also indicates that all the items may not be related.

##### How To Fix

Extract code out into smaller enums.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a enum. (Default 500)

#### MaxLinesInUnion - FS0032

##### Cause

A discriminated union is made up of more than a configurable number of lines.

##### Rationale

The larger a discriminated union becomes the more complex it becomes, it also indicates that all the items may not be related.

##### How To Fix

Extract code out into smaller composed discriminated unions.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a discriminated union. (Default 500)

#### MaxLinesInRecord - FS0030

##### Cause

A record is made up of more than a configurable number of lines.

##### Rationale

The larger a record becomes the more complex it becomes, it also indicates that all the items may not be related.

##### How To Fix

Extract code out into smaller composed records.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a record. (Default 500)

#### MaxLinesInModule - FS0029

##### Cause

A module is made up of more than a configurable number of lines.

##### Rationale

The larger a module becomes the more complex it becomes, it also indicates that it may have too many different responsibilities.

##### How To Fix

Extract code out into smaller modules.

##### Rule Settings

`enabled` - A boolean property that can enable and disable this rule. (Default true)
`maxLines` - Maximum number of lines in a module. (Default 1000)
