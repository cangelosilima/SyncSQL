# Synonym dependencies

SQL Server and Oracle synonyms remain catalog objects. Their `CREATE SYNONYM`
definitions link to the referenced object, preserving database and linked-server
qualifiers (or Oracle database links).

When an object uses a synonym, the catalog retains the dependency on the synonym
and adds a dependency on the ultimate observed target. Thus the target's **Used
by** list includes its synonyms and their consumers. Referenced columns are
validated against the target's extracted columns and attributed to that target.
Dynamic SQL dependencies remain marked dynamic.

Synonym chains are followed only through uniquely resolved, extracted objects.
Cycles and missing, ambiguous, or external targets do not produce an invented
underlying target. The synonym remains visible, with the usual unresolved or
external reference information on its definition.

Rebuild the catalog with the updated CLI to populate these relationships.
Include the `Synonyms` object type when extracting, and include remote targets
and linked-server/database-link metadata when cross-server resolution is needed.
