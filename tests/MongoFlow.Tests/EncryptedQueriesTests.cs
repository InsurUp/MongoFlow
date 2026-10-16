using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="EncryptedQueries{TDocument}"/> rewrites LINQ queries of a collection with encrypted members as they run:
/// <list type="number">
/// <item>a <c>Where</c> on the documents that compares an encrypted member becomes a <c>$match</c> of the encrypted
/// filter, before or after operators that keep the documents, and a terminal operator's predicate runs as one;</item>
/// <item>a query that compares no encrypted member, or only projects them, is left as it is;</item>
/// <item>an encrypted member sorted, grouped, joined on, compared in a projection, or filtered after it's projected
/// throws, as no filter MongoFlow encrypts could serve it;</item>
/// <item>a join on a collection whose query filters compare its encrypted members throws.</item>
/// </list>
/// </summary>
public partial class EncryptedQueriesTests
{
    [Test]
    public async Task Rewrite_WhereOnEncryptedMembers_MatchesTheEncryptedFilters()
    {
        // Arrange
        await using var host = Host();
        var query = (await host.Vault.Patients.QueryAsync())
            .Where(x => x.Ssn == "111-11")
            .OrderBy(x => x.Name)
            .Where(x => x.Age > 30 && x.Name != "Bob")
            .Where(x => x.Name == "Ada")
            .Take(5);

        // Act
        var rewritten = Rewritten(host, query);

        // Assert
        await Verify(rewritten.ToString());
    }

    [Test]
    public async Task Rewrite_TerminalPredicate_RunsItAsAWhere()
    {
        // Arrange
        await using var host = Host();
        var query = await host.Vault.Patients.QueryAsync();
        var first = Expression.Call(typeof(Queryable), nameof(Queryable.FirstOrDefault), [typeof(Patient)], query.Expression,
            Expression.Quote((Expression<Func<Patient, bool>>)(x => x.Ssn == "111-11")));

        // Act
        var rewritten = (MethodCallExpression)Model(host).EncryptedQueries!.Rewrite(first);

        // Assert
        await Verify(new { rewritten.Method.Name, Source = ((MethodCallExpression)rewritten.Arguments[0]).Method.Name });
    }

    [Test]
    public async Task Rewrite_NoEncryptedMemberCompared_LeavesTheQueryAsItIs()
    {
        // Arrange
        await using var host = Host();
        var query = (await host.Vault.Patients.QueryAsync())
            .Where(x => x.Name == "Ada")
            .OrderBy(x => x.Name.Length)
            .Select(x => new { x.Name, x.Ssn, Ward = x.Ward!.Name });

        // Act
        var rewritten = Model(host).EncryptedQueries!.Rewrite(query.Expression);

        // Assert
        await Assert.That(rewritten).IsSameReferenceAs(query.Expression);
    }

    [Test]
    public async Task Rewrite_QueryWithoutOperators_LeavesItAsItIs()
    {
        // Arrange
        await using var host = Host();
        var query = await host.Vault.Patients.QueryAsync();

        // Act
        var rewritten = Model(host).EncryptedQueries!.Rewrite(query.Expression);

        // Assert
        await Assert.That(rewritten).IsSameReferenceAs(query.Expression);
    }

    [Test]
    [Arguments("Sort")]
    [Arguments("Group")]
    [Arguments("ComparedInAProjection")]
    [Arguments("CalledInAProjection")]
    [Arguments("NegatedInAProjection")]
    [Arguments("TestedInAProjection")]
    [Arguments("ConvertedAndCompared")]
    [Arguments("FilteredAfterAProjection")]
    [Arguments("FilteredAfterAProjectionOfTheDocument")]
    [Arguments("JoinedOn")]
    public async Task Rewrite_EncryptedMemberNoFilterCanServe_ThrowsNotSupportedException(string use)
    {
        // Arrange
        await using var host = Host();
        var patients = await host.Vault.Patients.QueryAsync();
        var visits = await host.Vault.Visits.QueryAsync();
        IQueryable query = use switch
        {
            "Sort" => patients.OrderBy(x => x.Age),
            "Group" => patients.GroupBy(x => x.Ssn).Select(group => group.Key),
            "ComparedInAProjection" => patients.Select(x => new { x.Name, Adult = x.Age >= 18 }),
            "CalledInAProjection" => patients.Select(x => x.Ssn!.ToUpperInvariant()),
            "NegatedInAProjection" => patients.Select(x => -x.Age),
            "TestedInAProjection" => patients.Select(x => x.Insured ? "insured" : "not insured"),
            "ConvertedAndCompared" => patients.Select(x => (long)x.Age > 30L),
            "FilteredAfterAProjection" => patients.Select(x => new { x.Id, x.Age }).Where(x => x.Age > 30),
            "FilteredAfterAProjectionOfTheDocument" => patients.Select(x => x).Where(x => x.Age > 30),
            _ => patients.Join(visits, patient => patient.Ssn, visit => visit.PatientSsn, (patient, visit) => visit.Id)
        };

        // Act & Assert
        await Throws(() => Model(host).EncryptedQueries!.Rewrite(query.Expression)).IgnoreStackTrace().UseParameters(use);
    }

    [Test]
    public async Task QueryAsync_JoinOnACollectionFilteredByAnEncryptedMember_ThrowsNotSupportedException()
    {
        // Arrange — the patients' query filter compares an encrypted member, which a $lookup can't encrypt.
        await using var host = Host(patients => patients.QueryFilter(x => x.Ssn != "000-00"));
        var visits = await host.Vault.Visits.QueryAsync();
        var patients = await host.Vault.Patients.QueryAsync();

        // Act & Assert
        await Throws(() => visits.Join(patients, visit => visit.PatientSsn, patient => patient.Name, (visit, patient) => patient.Id))
            .IgnoreStackTrace();
    }

    private static VaultHost<ClinicVault> Host(Action<IVaultCollectionBuilder<Patient, int>>? patients = null) =>
        new(vault =>
        {
            ((VaultModelBuilderBase)vault).UseEncryption(new MarkingEncryption());
            vault.Collection(x => x.Patients, collection =>
            {
                var builder = (CollectionModelBuilder<Patient>)collection;
                builder.Encrypt((Expression<Func<Patient, string?>>)(x => x.Ssn), new object());
                builder.Encrypt((Expression<Func<Patient, int>>)(x => x.Age), new object());
                builder.Encrypt((Expression<Func<Patient, bool>>)(x => x.Insured), new object());
                patients?.Invoke(collection);
            });
        });

    private static CollectionModel<Patient> Model(VaultHost<ClinicVault> host) =>
        (CollectionModel<Patient>)host.Services.GetRequiredService<VaultModelProvider<ClinicVault>>().Model.Collections
            .Single(collection => collection.DocumentType == typeof(Patient));

    private static IQueryable<T> Rewritten<T>(VaultHost<ClinicVault> host,
        IQueryable<T> query) =>
        query.Provider.CreateQuery<T>(Model(host).EncryptedQueries!.Rewrite(query.Expression));
}
